using BLL.Algorithms;
using BLL.Configuration;
using BLL.Interfaces; // ודא שקיים עבור IDonationRepository וכו'
using DAL.Classes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    public class DonationMatchingOrchestrator
    {
        private readonly IDonationRepository _donationRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly KnapsackSolver _knapsackSolver;
        private readonly IRouteOptimizationService _routeOptimizationService;
        private readonly MatchingPolicyOptions _policyOptions;
        private readonly ILogger<DonationMatchingOrchestrator> _logger;

        public DonationMatchingOrchestrator(
            KnapsackSolver knapsackSolver,
            IRouteOptimizationService routeOptimizationService,
            IOptions<MatchingPolicyOptions> policyOptions,
            ILogger<DonationMatchingOrchestrator> logger,
            IDonationRepository donationRepository,
            IVolunteerRepository volunteerRepository,
            INgoRepository ngoRepository)
        {
            _knapsackSolver = knapsackSolver;
            _routeOptimizationService = routeOptimizationService;
            _policyOptions = policyOptions?.Value ?? new MatchingPolicyOptions();
            _logger = logger;
            _donationRepository = donationRepository;
            _volunteerRepository = volunteerRepository;
            _ngoRepository = ngoRepository;
        }

        internal bool IsWithinPickupDeadline(PickupStop stop, Donation donation)
        {
            if (stop == null || donation == null) return false;
            if (!stop.EstimatedArrival.HasValue) return false;
            var pickupDateTime = NgoAvailabilityHelper.GetPickupDateTime(donation);
            return stop.EstimatedArrival.Value <= pickupDateTime;
        }

        internal bool VolunteerAvailabilityCoversRoute(Volunteer volunteer, DateTime routeStart, DateTime routeEnd, List<Donation> compatibleDonations, Ngo targetNgo)
        {
            if (compatibleDonations == null || compatibleDonations.Count == 0) return false;

            var donationRegionIds = compatibleDonations.Select(d => d.RegionId).Distinct().ToList();
            return AvailabilityHelper.CoversRoute(volunteer, routeStart, routeEnd, donationRegionIds, targetNgo.RegionId);
        }

        public async Task RunMatchingCycleAsync()
        {
            _logger?.LogInformation("Manual matching cycle triggered.");

            var availableDonations = await _donationRepository.GetAvailableDonationsAsync();
            if (availableDonations == null || !availableDonations.Any())
            {
                _logger?.LogInformation("Matching cycle: No available donations found.");
                return;
            }

            var volunteers = await _volunteerRepository.GetAllAsync();
            var ngos = await _ngoRepository.GetAllAsync();
            var targetNgo = ngos.FirstOrDefault();

            if (targetNgo == null)
            {
                _logger?.LogWarning("Matching cycle: No target NGO found.");
                return;
            }

            foreach (var volunteer in volunteers)
            {
                var startLocation = new GeoCoordinate { Latitude = 0, Longitude = 0 };

                var route = await ProcessVolunteerRouteAssignmentAsync(
                    volunteer,
                    availableDonations.ToList(),
                    startLocation,
                    targetNgo);

                if (route != null && route.Any())
                {
                    _logger?.LogInformation("Matching cycle: Route generated successfully for volunteer {Id}.", volunteer.Id);
                }
            }
        }

        public async Task<List<PickupStop>> ProcessVolunteerRouteAssignmentAsync(
            Volunteer volunteer,
            List<Donation> availableDonations,
            GeoCoordinate volunteerStartLocation,
            Ngo targetNgo,
            DateTime? startTime = null,
            int? anchorDonationId = null)
        {
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));

            var effectiveStartLocation = volunteerStartLocation;
            if (effectiveStartLocation == null || (effectiveStartLocation.Latitude == 0 && effectiveStartLocation.Longitude == 0))
            {
                var currentDay = DateTime.UtcNow.DayOfWeek;
                var slot = volunteer.AvailabilitySlots?.FirstOrDefault(a => a.Day == currentDay);

                if (slot != null && slot.RegionId > 0)
                {
                    _logger?.LogInformation("Orchestrator [Volunteer {VolunteerId}]: Volunteer has 0,0 location. Fallback logic triggered for RegionId {RegionId}.", volunteer.Id, slot.RegionId);
                }

                if (effectiveStartLocation == null)
                {
                    effectiveStartLocation = new GeoCoordinate { Latitude = 0, Longitude = 0 };
                }
            }

            var compatibleDonations = availableDonations.Where(d => NgoAvailabilityHelper.CanNgoReceiveDonation(targetNgo, d)).ToList();

            var startTimeValue = startTime ?? DateTime.UtcNow;

            var anchorDonation = anchorDonationId.HasValue
                ? compatibleDonations.FirstOrDefault(d => d.Id == anchorDonationId.Value)
                : null;

            double centerLat = anchorDonation?.Latitude ?? effectiveStartLocation.Latitude;
            double centerLon = anchorDonation?.Longitude ?? effectiveStartLocation.Longitude;
            double clusterRadiusKm = anchorDonation != null ? _policyOptions.ClusterRadiusKm : _policyOptions.MaxSearchRadiusKm;

            var knapsackItems = KnapsackItem.CreateItems(
                compatibleDonations,
                centerLat, centerLon,
                clusterRadiusKm,
                _policyOptions.DistancePenaltyCoefficient,
                startTimeValue,
                _policyOptions.UnitSizeGrams,
                _policyOptions.UrgencyTimeConstantMinutes,
                anchorDonationId);

            int maxCapacityUnits = (int)((volunteer.MaxCapacityKg * 1000) / _policyOptions.UnitSizeGrams);
            int maxItemsPerCategory = _policyOptions.MaxItemsPerCategory;

            var knapsackResult = _knapsackSolver.Solve(knapsackItems, maxCapacityUnits, maxItemsPerCategory, anchorDonationId);
            var selectedDonations = knapsackResult.SelectedDonations;

            if (selectedDonations.Count == 0)
            {
                _logger?.LogInformation("Orchestrator [Volunteer {VolunteerId}, Anchor {AnchorId}]: Knapsack selected count: 0 — early exit.", volunteer.Id, anchorDonationId ?? -1);
                return new List<PickupStop>();
            }

            var stops = selectedDonations.Select(d => new PickupStop
            {
                DonationId = d.Id,
                Location = new GeoCoordinate
                {
                    Latitude = d.Latitude,
                    Longitude = d.Longitude
                }
            }).ToList();

            List<PickupStop> optimizedRoute;

            try
            {
                optimizedRoute = await _routeOptimizationService.OptimizeRouteWithGoogleMapsAsync(
                    effectiveStartLocation, stops, startTimeValue,
                    (stop, eta) =>
                    {
                        var d = compatibleDonations.FirstOrDefault(x => x.Id == stop.DonationId);
                        var deadline = d != null ? NgoAvailabilityHelper.GetPickupDateTime(d) : (DateTime?)null;
                        return d != null && eta <= deadline;
                    });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Google Maps route optimization failed for volunteer {VolunteerId}. Falling back.", volunteer.Id);
                optimizedRoute = _routeOptimizationService.OptimizeRoute(effectiveStartLocation, stops, startTimeValue);
            }

            if (optimizedRoute == null || optimizedRoute.Count == 0)
            {
                return new List<PickupStop>();
            }

            var lastStop = optimizedRoute.Last();
            if (!lastStop.EstimatedArrival.HasValue)
            {
                return new List<PickupStop>();
            }
            var ngoArrival = lastStop.EstimatedArrival.Value;
            if (!NgoAvailabilityHelper.IsNgoOpenAt(targetNgo, ngoArrival))
            {
                return new List<PickupStop>();
            }

            foreach (var stop in optimizedRoute)
            {
                var donation = compatibleDonations.FirstOrDefault(d => d.Id == stop.DonationId);
                if (donation == null || !stop.EstimatedArrival.HasValue || !IsWithinPickupDeadline(stop, donation))
                {
                    return new List<PickupStop>();
                }
            }

            var routeStart = startTimeValue;
            var routeEnd = lastStop.EstimatedArrival.Value;

            if (!VolunteerAvailabilityCoversRoute(volunteer, routeStart, routeEnd, compatibleDonations, targetNgo))
            {
                return new List<PickupStop>();
            }

            if (anchorDonationId.HasValue && !optimizedRoute.Any(s => s.DonationId == anchorDonationId.Value))
            {
                return new List<PickupStop>();
            }

            return optimizedRoute;
        }
    }
}