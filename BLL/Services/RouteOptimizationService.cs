using BLL.Algoritems;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using Microsoft.Extensions.Options;
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
        public GeoCoordinate Location { get; set; }
        public DateTime? EstimatedArrival { get; set; }
    }

    /// <summary>
    /// אלגוריתם Nearest-Neighbor לבניית מסלול איסוף.
    ///
    /// תיקונים לעומת הגרסה המקורית:
    /// 1. בגרסה הסינכרונית, בחירת ה"עצירה הקרובה ביותר" הייתה מבוססת על מרחק אוקלידי גולמי
    ///    במעלות (lat/lon) בעוד שזמן הנסיעה בפועל חושב לפי Haversine בק"מ - שני קריטריונים
    ///    שונים שיכולים להוביל לבחירת עצירה "הלא נכונה" (מעלת קו רוחב ומעלת קו אורך אינן
    ///    מרחק שווה בק"מ). כעת שתי הפעולות (בחירה וחישוב זמן) מסתמכות על אותה פונקציית מרחק.
    /// 2. לא נלקח בחשבון זמן שירות בעצירה (העמסה/חתימה) - כעת מתווסף StopServiceMinutes
    ///    מתוך המדיניות אחרי כל עצירה, כך שזמן ההגעה לעצירה הבאה ריאלי יותר.
    /// 3. כאשר Google Maps לא זמין (סינכרוני, או Fallback באסינכרוני), קו אווירי מוכפל
    ///    במקדם עקיפת-כביש (RoadDetourFactor) כדי לא להעריך בחסר את זמן/מרחק הנסיעה בפועל.
    /// 4. מהירות ברירת מחדל (AverageSpeedKmH) נלקחת מהקונפיגורציה המרכזית (MatchingPolicyOptions)
    ///    במקום קבוע 40.0 מקודד בקוד בשני מקומות שונים.
    /// </summary>
    public class RouteOptimizationService : IRouteOptimizationService
    {
        private readonly IGoogleMapsDistanceService _googleMapsDistanceService;
        private readonly MatchingPolicyOptions _options;

        public RouteOptimizationService(
            IGoogleMapsDistanceService googleMapsDistanceService = null,
            IOptions<MatchingPolicyOptions> options = null)
        {
            _googleMapsDistanceService = googleMapsDistanceService;
            _options = options?.Value ?? new MatchingPolicyOptions();
        }

        public List<PickupStop> OptimizeRoute(GeoCoordinate startPoint, List<PickupStop> stops, DateTime startTime)
        {
            if (startPoint == null) throw new ArgumentNullException(nameof(startPoint));
            if (stops == null || stops.Count == 0) return new List<PickupStop>();

            var optimizedRoute = new List<PickupStop>(stops.Count);
            var remainingStops = new List<PickupStop>(stops);
            var currentPos = startPoint;
            var currentTime = startTime;

            while (remainingStops.Count > 0)
            {
                PickupStop nearestStop = null;
                double bestDistanceKm = double.MaxValue;

                foreach (var stop in remainingStops)
                {
                    // אותה פונקציית מרחק (Haversine) משמשת גם לבחירה וגם לחישוב הזמן,
                    // כדי שהעצירה שנבחרת כ"קרובה ביותר" תהיה אכן זו שדורשת הכי פחות נסיעה.
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
                    bestDistanceKm, _options.AverageSpeedKmH, _options.RoadDetourFactor);

                currentTime = currentTime.AddMinutes(travelMinutes);
                nearestStop.EstimatedArrival = currentTime;

                optimizedRoute.Add(nearestStop);
                currentPos = nearestStop.Location;
                remainingStops.Remove(nearestStop);

                // זמן שירות בעצירה (העמסה/מסירה) - נספר לפני היציאה לעצירה הבאה.
                currentTime = currentTime.AddMinutes(_options.StopServiceMinutes);
            }

            return optimizedRoute;
        }

        public async Task<List<PickupStop>> OptimizeRouteWithGoogleMapsAsync(
            GeoCoordinate startPoint,
            List<PickupStop> stops,
            DateTime startTime,
            Func<PickupStop, DateTime, bool> isArrivalAcceptable = null)
        {
            if (startPoint == null) throw new ArgumentNullException(nameof(startPoint));
            if (stops == null || stops.Count == 0) return new List<PickupStop>();
            if (_googleMapsDistanceService == null)
                throw new InvalidOperationException(
                    "לא הוזרק IGoogleMapsDistanceService - השתמשי ב-OptimizeRoute הסינכרוני, או ספקי מימוש בבנאי.");

            var optimizedRoute = new List<PickupStop>(stops.Count);
            var remainingStops = new List<PickupStop>(stops);
            var currentPos = startPoint;
            var currentTime = startTime;

            while (remainingStops.Count > 0)
            {
                PickupStop nearestStop = null;
                double bestDistanceKm = double.MaxValue;
                double bestDurationMinutes = 0;

                foreach (var stop in remainingStops)
                {
                    double distanceKm;
                    double durationMinutes;
                    try
                    {
                        var result = await _googleMapsDistanceService.GetDrivingDistanceAsync(
                            currentPos.Latitude, currentPos.Longitude,
                            stop.Location.Latitude, stop.Location.Longitude);
                        distanceKm = result.DistanceKm;
                        durationMinutes = result.DurationMinutes;
                    }
                    catch
                    {
                        // גיבוי: קו אווירי + מקדם עקיפת-כביש, במקום הנחת "קו אווירי = כביש"
                        distanceKm = GeoUtils.HaversineKm(
                            currentPos.Latitude, currentPos.Longitude,
                            stop.Location.Latitude, stop.Location.Longitude);
                        durationMinutes = GeoUtils.EstimateDriveMinutes(
                            distanceKm, _options.AverageSpeedKmH, _options.RoadDetourFactor);
                    }

                    var candidateArrival = currentTime.AddMinutes(durationMinutes);
                    if (isArrivalAcceptable != null && !isArrivalAcceptable(stop, candidateArrival))
                    {
                        continue;
                    }

                    if (distanceKm < bestDistanceKm)
                    {
                        bestDistanceKm = distanceKm;
                        nearestStop = stop;
                        bestDurationMinutes = durationMinutes;
                    }
                }

                if (nearestStop == null)
                {
                    // אין אף עצירה שעומדת באילוץ ה-isArrivalAcceptable מהמיקום הנוכחי - עוצרים,
                    // בדיוק כמו בגרסה המקורית (הקוד הקורא צריך להתמודד עם מסלול חלקי).
                    break;
                }

                currentTime = currentTime.AddMinutes(bestDurationMinutes);
                nearestStop.EstimatedArrival = currentTime;
                optimizedRoute.Add(nearestStop);
                currentPos = nearestStop.Location;
                remainingStops.Remove(nearestStop);

                currentTime = currentTime.AddMinutes(_options.StopServiceMinutes);
            }

            return optimizedRoute;
        }
    }
}
