using BLL.Algoritems;
using BLL.Configuration;
using DAL;
using DAL.Classes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    public class RegionAvailabilityMatchingService
    {
        private readonly IMatchingPolicyProvider _policyProvider;
        private readonly ILogger<RegionAvailabilityMatchingService> _logger;

        public RegionAvailabilityMatchingService(
            ILogger<RegionAvailabilityMatchingService>? logger = null,
            IMatchingPolicyProvider? policy = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RegionAvailabilityMatchingService>.Instance;
            _policyProvider = policy ?? new StaticMatchingPolicyProvider();
        }

        public List<Volunteer> FindCandidates(
            Donation donation,
            Ngo targetNgo,
            IEnumerable<Volunteer> allVolunteers,
            DateTime asOf)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));
            if (allVolunteers == null) throw new ArgumentNullException(nameof(allVolunteers));

            var _policy = _policyProvider.Current;
            var localAsOf = LocalClock.ToLocal(asOf);
            var currentDay = localAsOf.DayOfWeek;
            var currentTime = localAsOf.TimeOfDay;
            var donationRegions = new[] { donation.RegionId };

            var pickupDateTime = NgoAvailabilityHelper.GetPickupDateTime(donation);

            _logger.LogDebug("[MatchingDebug] === Starting FindCandidates for DonationId={DonationId}, NgoId={NgoId} ===", donation.Id, targetNgo.Id);
            _logger.LogDebug("[MatchingDebug] LocalAsOf={LocalAsOf} | Day={Day} | Time={Time} | DonationRegion={DonationRegion} | NgoRegion={NgoRegion}", localAsOf, currentDay, currentTime, donation.RegionId, targetNgo.RegionId);

            var filtered = new List<Volunteer>();

            foreach (var v in allVolunteers)
            {
                _logger.LogDebug("[MatchingDebug] Checking Volunteer ID={VolunteerId} | IsAvailable={IsAvailable} | SlotsCount={SlotsCount}", v.Id, v.IsAvailable, v.AvailabilitySlots?.Count ?? 0);

                if (!v.IsAvailable)
                {
                    _logger.LogDebug("[MatchingDebug]   -> Skipped: Volunteer is not available (IsAvailable = false).");
                    continue;
                }

                if (v.AvailabilitySlots == null || !v.AvailabilitySlots.Any())
                {
                    _logger.LogDebug("[MatchingDebug]   -> Skipped: Volunteer has no availability slots.");
                    continue;
                }

                bool matchedSlot = false;
                foreach (var slot in v.AvailabilitySlots)
                {
                    bool dayMatch = slot.Day == currentDay;
                    bool timeMatch = AvailabilityHelper.IsWithinTimeWindow(slot, currentTime);
                    bool routeMatch = AvailabilityHelper.SlotServesRoute(slot, donationRegions, targetNgo.RegionId);

                    _logger.LogDebug("[MatchingDebug]     Slot ID={SlotId}: Day={SlotDay} (Match:{DayMatch}), Time={StartTime}-{EndTime} (Match:{TimeMatch}), Region={RegionId}, ToRegion={ToRegionId} (RouteMatch:{RouteMatch})",
                        slot.Id, slot.Day, dayMatch, slot.StartTime, slot.EndTime, timeMatch, slot.RegionId, slot.ToRegionId, routeMatch);

                    if (dayMatch && timeMatch && routeMatch)
                    {
                        matchedSlot = true;
                        break;
                    }
                }

                if (!matchedSlot)
                {
                    _logger.LogDebug("[MatchingDebug]   -> Skipped: No matching slot found for current day, time, and route.");
                    continue;
                }

                bool hasValidLocation = (v.CurrentLatitude != 0 || v.CurrentLongitude != 0) &&
                                        v.LocationUpdatedAt.HasValue &&
                                        (asOf - v.LocationUpdatedAt.Value).TotalMinutes <= _policy.MaxLocationAgeMinutes;

                if (hasValidLocation)
                {
                    var distanceKm = GeoUtils.HaversineKm(v.CurrentLatitude, v.CurrentLongitude, donation.Latitude, donation.Longitude);
                    var travelMinutes = GeoUtils.EstimateDriveMinutes(distanceKm, _policy.AverageSpeedKmH, _policy.RoadDetourFactor);
                    var estimatedArrival = asOf.AddMinutes(travelMinutes);

                    _logger.LogDebug("[MatchingDebug]   -> Has valid GPS. Distance={Distance}km, TravelMinutes={Minutes}, EstimatedArrival={Arrival}, PickupDeadline={Deadline}", distanceKm, travelMinutes, estimatedArrival, pickupDateTime);

                    if (estimatedArrival <= pickupDateTime)
                    {
                        filtered.Add(v);
                        _logger.LogDebug("[MatchingDebug]   -> APPROVED (GPS check passed).");
                    }
                    else
                    {
                        _logger.LogDebug("[MatchingDebug]   -> Skipped: Estimated arrival is too late.");
                    }
                }
                else
                {
                    filtered.Add(v);
                    _logger.LogDebug("[MatchingDebug]   -> APPROVED (No GPS / Fallback mode used).");
                }
            }

            _logger.LogDebug("[MatchingDebug] === Finished. Total candidates found: {Count} ===", filtered.Count);
            return filtered.Distinct().ToList();
        }
    }
}