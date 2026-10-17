using BLL.Configuration;
using DAL.Classes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    // מסלול מוצע למתנדב: כמה תורמים -> עמותה אחת
    public sealed class RoutePlan
    {
        public List<PickupStop> Stops { get; init; } = new();
        public GeoCoordinate StartLocation { get; init; } = new();
        public DateTime StartTime { get; init; }
        public DateTime NgoArrival { get; init; }
        public double NgoLegDistanceKm { get; init; }
        public double NgoLegDurationMinutes { get; init; }
        public double TotalDistanceKm { get; init; }
        public double TotalDurationMinutes { get; init; }
        public double TotalWeightKg { get; init; }
        public double KnapsackValue { get; init; }
    }

    /// <summary>
    /// בונה מסלול אפשרי אחד למתנדב ולעמותה נתונים: Knapsack (קיבולת, תרומה במלואה) + VRP (סדר התחנות)
    /// ובדיקת כל האילוצים הקשים: זמן האיסוף האחרון של כל תרומה, תפוגה, שעות הקבלה של העמותה בזמן
    /// ההגעה בפועל, וזמינות המתנדב לכל משך המסלול.
    ///
    /// תוקן: (1) זמן ההגעה לעמותה חושב בעבר כזמן ההגעה לתחנת האיסוף האחרונה (בלי הנסיעה לעמותה).
    /// (2) הוסרה הפונקציה RunMatchingCycleAsync - מסלול שידוך שלישי שהתחיל מנקודה (0,0), בחר תמיד את
    /// העמותה הראשונה ולא שמר כלום. כל השידוכים עוברים עכשיו דרך DonationMatchingBatchService.
    /// </summary>
    public class DonationMatchingOrchestrator
    {
        private readonly KnapsackSolver _knapsackSolver;
        private readonly IRouteOptimizationService _routeOptimizationService;
        private readonly IMatchingPolicyProvider _policy;
        private readonly ILogger<DonationMatchingOrchestrator> _logger;

        public DonationMatchingOrchestrator(
            KnapsackSolver knapsackSolver,
            IRouteOptimizationService routeOptimizationService,
            IMatchingPolicyProvider policy,
            ILogger<DonationMatchingOrchestrator> logger)
        {
            _knapsackSolver = knapsackSolver;
            _routeOptimizationService = routeOptimizationService;
            _policy = policy;
            _logger = logger;
        }

        internal bool IsWithinPickupDeadline(PickupStop stop, Donation donation)
        {
            if (stop == null || donation == null) return false;
            if (!stop.EstimatedArrival.HasValue) return false;
            var pickupDateTime = NgoAvailabilityHelper.GetPickupDateTime(donation);
            return stop.EstimatedArrival.Value <= pickupDateTime;
        }

        internal bool VolunteerAvailabilityCoversRoute(Volunteer volunteer, DateTime routeStart, DateTime routeEnd, List<Donation> routeDonations, Ngo targetNgo)
        {
            if (routeDonations == null || routeDonations.Count == 0) return false;

            var donationRegionIds = routeDonations.Select(d => d.RegionId).Distinct().ToList();
            return AvailabilityHelper.CoversRoute(volunteer, routeStart, routeEnd, donationRegionIds, targetNgo.RegionId);
        }

        // תאימות: מחזיר רק את התחנות (רשימה ריקה = אין מסלול אפשרי)
        public async Task<List<PickupStop>> ProcessVolunteerRouteAssignmentAsync(
            Volunteer volunteer,
            List<Donation> availableDonations,
            GeoCoordinate volunteerStartLocation,
            Ngo targetNgo,
            DateTime? startTime = null,
            int? anchorDonationId = null)
        {
            var plan = await BuildRouteAsync(volunteer, availableDonations, volunteerStartLocation, targetNgo, startTime, anchorDonationId);
            return plan?.Stops ?? new List<PickupStop>();
        }

        public async Task<RoutePlan?> BuildRouteAsync(
            Volunteer volunteer,
            List<Donation> availableDonations,
            GeoCoordinate volunteerStartLocation,
            Ngo targetNgo,
            DateTime? startTime = null,
            int? anchorDonationId = null)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));

            var policy = _policy.Current;
            var start = volunteerStartLocation ?? new GeoCoordinate();
            var startTimeValue = startTime ?? DateTime.UtcNow;

            // אילוץ קשה: העמותה יכולה לקבל את התרומה בזמן (פתוחה בזמן ההגעה האפשרי ולפני התפוגה)
            var compatibleDonations = availableDonations
                .Where(d => d.Status == DonationStatus.Pending &&
                            NgoAvailabilityHelper.CanNgoReceiveDonation(targetNgo, d, policy, startTimeValue))
                .ToList();

            var anchorDonation = anchorDonationId.HasValue
                ? compatibleDonations.FirstOrDefault(d => d.Id == anchorDonationId.Value)
                : null;

            if (anchorDonationId.HasValue && anchorDonation == null)
            {
                _logger.LogDebug("Orchestrator: anchor donation {DonationId} cannot reach NGO {NgoId} in time.", anchorDonationId, targetNgo.Id);
                return null;
            }

            double centerLat = anchorDonation?.Latitude ?? start.Latitude;
            double centerLon = anchorDonation?.Longitude ?? start.Longitude;
            double clusterRadiusKm = anchorDonation != null ? policy.ClusterRadiusKm : policy.MaxSearchRadiusKm;

            // Knapsack 0/1: כל תרומה נכנסת במלואה או לא נכנסת, במסגרת קיבולת הרכב
            var knapsackItems = KnapsackItem.CreateItems(
                compatibleDonations,
                centerLat, centerLon,
                clusterRadiusKm,
                policy.DistancePenaltyCoefficient,
                startTimeValue,
                policy.UnitSizeGrams,
                policy.UrgencyTimeConstantMinutes,
                anchorDonationId);

            int maxCapacityUnits = (int)((volunteer.MaxCapacityKg * 1000) / Math.Max(1, policy.UnitSizeGrams));
            var knapsackResult = _knapsackSolver.Solve(knapsackItems, maxCapacityUnits, policy.MaxItemsPerCategory, anchorDonationId);
            var selectedDonations = knapsackResult.SelectedDonations;

            if (selectedDonations.Count == 0)
                return null;

            var stops = selectedDonations.Select(d => new PickupStop
            {
                DonationId = d.Id,
                Location = new GeoCoordinate { Latitude = d.Latitude, Longitude = d.Longitude }
            }).ToList();

            List<PickupStop> optimizedRoute;
            try
            {
                optimizedRoute = await _routeOptimizationService.OptimizeRouteWithGoogleMapsAsync(
                    start, stops, startTimeValue,
                    (stop, eta) =>
                    {
                        var d = compatibleDonations.FirstOrDefault(x => x.Id == stop.DonationId);
                        return d != null && eta <= NgoAvailabilityHelper.GetPickupDateTime(d);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Route optimization with Google failed for volunteer {VolunteerId}. Falling back.", volunteer.Id);
                optimizedRoute = _routeOptimizationService.OptimizeRoute(start, stops, startTimeValue);
            }

            if (optimizedRoute == null || optimizedRoute.Count == 0)
                return null;

            if (anchorDonationId.HasValue && !optimizedRoute.Any(s => s.DonationId == anchorDonationId.Value))
                return null;

            // כל תחנה במסלול: לפני זמן האיסוף האחרון של התרומה שלה
            var routeDonations = new List<Donation>(optimizedRoute.Count);
            foreach (var stop in optimizedRoute)
            {
                var donation = compatibleDonations.FirstOrDefault(d => d.Id == stop.DonationId);
                if (donation == null || !IsWithinPickupDeadline(stop, donation))
                    return null;
                routeDonations.Add(donation);
            }

            // הקטע האחרון: מהתחנה האחרונה לעמותה
            var lastStop = optimizedRoute[^1];
            var ngoLocation = new GeoCoordinate { Latitude = targetNgo.Latitude, Longitude = targetNgo.Longitude };
            var (ngoLegKm, ngoLegMinutes) = await _routeOptimizationService.EstimateLegAsync(lastStop.Location, ngoLocation);
            var ngoArrival = lastStop.EstimatedArrival!.Value
                .AddMinutes(policy.StopServiceMinutes)
                .AddMinutes(Math.Max(0, ngoLegMinutes));

            // העמותה פתוחה בזמן ההגעה בפועל
            if (!NgoAvailabilityHelper.IsNgoOpenAt(targetNgo, ngoArrival))
                return null;

            // המזון מגיע לעמותה לפני שפג תוקפו
            if (routeDonations.Any(d => ngoArrival > d.ExpirationTime))
                return null;

            // המתנדב זמין לכל משך המסלול, באזורי היציאה והיעד שהגדיר
            if (!VolunteerAvailabilityCoversRoute(volunteer, startTimeValue, ngoArrival, routeDonations, targetNgo))
                return null;

            double totalKm = optimizedRoute.Sum(s => s.LegDistanceKm ?? 0) + ngoLegKm;
            double knapsackValue = knapsackItems
                .Where(i => optimizedRoute.Any(s => s.DonationId == i.Donation.Id))
                .Sum(i => i.Value);

            return new RoutePlan
            {
                Stops = optimizedRoute,
                StartLocation = start,
                StartTime = startTimeValue,
                NgoArrival = ngoArrival,
                NgoLegDistanceKm = ngoLegKm,
                NgoLegDurationMinutes = ngoLegMinutes,
                TotalDistanceKm = totalKm,
                TotalDurationMinutes = (ngoArrival - startTimeValue).TotalMinutes,
                TotalWeightKg = routeDonations.Sum(d => d.TotalWeightKg()),
                KnapsackValue = knapsackValue
            };
        }
    }
}
