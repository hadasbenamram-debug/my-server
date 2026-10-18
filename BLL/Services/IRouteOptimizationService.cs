using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    public interface IRouteOptimizationService
    {
        // מסלול לפי קו אווירי בלבד (גיבוי כש-Google לא זמין)
        List<PickupStop> OptimizeRoute(GeoCoordinate startPoint, List<PickupStop> stops, DateTime startTime);

        // מסלול Nearest-Neighbor לפי מרחק/זמן נסיעה אמיתי (Google, עם מטמון)
        Task<List<PickupStop>> OptimizeRouteWithGoogleMapsAsync(
            GeoCoordinate startPoint,
            List<PickupStop> stops,
            DateTime startTime,
            Func<PickupStop, DateTime, bool>? isArrivalAcceptable = null);

        // מרחק וזמן נסיעה לקטע אחד (למשל מהתחנה האחרונה לעמותה)
        Task<(double DistanceKm, double DurationMinutes)> EstimateLegAsync(GeoCoordinate from, GeoCoordinate to);
    }
}
