using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    public interface IRouteOptimizationService
    {
        List<PickupStop> OptimizeRoute(GeoCoordinate startPoint, List<PickupStop> stops, DateTime startTime);

        Task<List<PickupStop>> OptimizeRouteWithGoogleMapsAsync(
            GeoCoordinate startPoint,
            List<PickupStop> stops,
            DateTime startTime,
            Func<PickupStop, DateTime, bool> isArrivalAcceptable = null);
    }
}