using BLL.Algoritems;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    public class GeoCoordinate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class PickupStop
    {
        public int DonationId { get; set; }
        public GeoCoordinate Location { get; set; } = new GeoCoordinate();
        public DateTime? EstimatedArrival { get; set; }

        // הקטע שמוביל לתחנה הזו (מהתחנה הקודמת / מנקודת היציאה)
        public double? LegDistanceKm { get; set; }
        public double? LegDurationMinutes { get; set; }
    }

    /// <summary>
    /// אלגוריתם Nearest-Neighbor לבניית מסלול איסוף.
    /// בחירת התחנה הבאה וחישוב הזמן מבוססים על אותה פונקציית מרחק, כולל זמן שירות בכל תחנה
    /// (StopServiceMinutes) ומקדם עקיפת-כביש כשאין נתון מ-Google.
    /// </summary>
    public class RouteOptimizationService : IRouteOptimizationService
    {
        private readonly IGoogleMapsDistanceService? _googleMapsDistanceService;
        private readonly IMatchingPolicyProvider _policy;

        public RouteOptimizationService(
            IGoogleMapsDistanceService? googleMapsDistanceService = null,
            IMatchingPolicyProvider? policy = null)
        {
            _googleMapsDistanceService = googleMapsDistanceService;
            _policy = policy ?? new StaticMatchingPolicyProvider();
        }

        public List<PickupStop> OptimizeRoute(GeoCoordinate startPoint, List<PickupStop> stops, DateTime startTime)
        {
            if (startPoint == null) throw new ArgumentNullException(nameof(startPoint));
            if (stops == null || stops.Count == 0) return new List<PickupStop>();

            var options = _policy.Current;
            var optimizedRoute = new List<PickupStop>(stops.Count);
            var remainingStops = new List<PickupStop>(stops);
            var currentPos = startPoint;
            var currentTime = startTime;

            while (remainingStops.Count > 0)
            {
                PickupStop nearestStop = remainingStops[0];
                double bestDistanceKm = double.MaxValue;

                foreach (var stop in remainingStops)
                {
                    var distanceKm = GeoUtils.HaversineKm(
                        currentPos.Latitude, currentPos.Longitude,
                        stop.Location.Latitude, stop.Location.Longitude);

                    if (distanceKm < bestDistanceKm)
                    {
                        bestDistanceKm = distanceKm;
                        nearestStop = stop;
                    }
                }

                var travelMinutes = GeoUtils.EstimateDriveMinutes(
                    bestDistanceKm, options.AverageSpeedKmH, options.RoadDetourFactor);

                currentTime = currentTime.AddMinutes(travelMinutes);
                nearestStop.EstimatedArrival = currentTime;
                nearestStop.LegDistanceKm = bestDistanceKm * Math.Max(options.RoadDetourFactor, 1.0);
                nearestStop.LegDurationMinutes = travelMinutes;

                optimizedRoute.Add(nearestStop);
                currentPos = nearestStop.Location;
                remainingStops.Remove(nearestStop);

                // זמן שירות בעצירה (העמסה/מסירה) - נספר לפני היציאה לעצירה הבאה.
                currentTime = currentTime.AddMinutes(options.StopServiceMinutes);
            }

            return optimizedRoute;
        }

        public async Task<List<PickupStop>> OptimizeRouteWithGoogleMapsAsync(
            GeoCoordinate startPoint,
            List<PickupStop> stops,
            DateTime startTime,
            Func<PickupStop, DateTime, bool>? isArrivalAcceptable = null)
        {
            if (startPoint == null) throw new ArgumentNullException(nameof(startPoint));
            if (stops == null || stops.Count == 0) return new List<PickupStop>();
            if (_googleMapsDistanceService == null)
                throw new InvalidOperationException("IGoogleMapsDistanceService is not available - use OptimizeRoute.");

            var options = _policy.Current;
            var optimizedRoute = new List<PickupStop>(stops.Count);
            var remainingStops = new List<PickupStop>(stops);
            var currentPos = startPoint;
            var currentTime = startTime;

            while (remainingStops.Count > 0)
            {
                PickupStop? nearestStop = null;
                double bestDistanceKm = double.MaxValue;
                double bestDurationMinutes = 0;

                foreach (var stop in remainingStops)
                {
                    var (distanceKm, durationMinutes) = await EstimateLegAsync(currentPos, stop.Location);

                    var candidateArrival = currentTime.AddMinutes(durationMinutes);
                    if (isArrivalAcceptable != null && !isArrivalAcceptable(stop, candidateArrival))
                        continue;

                    if (distanceKm < bestDistanceKm)
                    {
                        bestDistanceKm = distanceKm;
                        nearestStop = stop;
                        bestDurationMinutes = durationMinutes;
                    }
                }

                // אין עוד תחנה שאפשר להגיע אליה בזמן - המסלול נעצר כאן (המתזמר בודק שתרומת העוגן בפנים)
                if (nearestStop == null)
                    break;

                currentTime = currentTime.AddMinutes(bestDurationMinutes);
                nearestStop.EstimatedArrival = currentTime;
                nearestStop.LegDistanceKm = bestDistanceKm;
                nearestStop.LegDurationMinutes = bestDurationMinutes;
                optimizedRoute.Add(nearestStop);
                currentPos = nearestStop.Location;
                remainingStops.Remove(nearestStop);

                currentTime = currentTime.AddMinutes(options.StopServiceMinutes);
            }

            return optimizedRoute;
        }

        public async Task<(double DistanceKm, double DurationMinutes)> EstimateLegAsync(GeoCoordinate from, GeoCoordinate to)
        {
            if (_googleMapsDistanceService != null)
            {
                try
                {
                    return await _googleMapsDistanceService.GetDrivingDistanceAsync(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
                }
                catch
                {
                    // נופלים להערכה לפי קו אווירי
                }
            }

            return GoogleMapsDistanceService.Estimate(from.Latitude, from.Longitude, to.Latitude, to.Longitude, _policy.Current);
        }
    }
}
