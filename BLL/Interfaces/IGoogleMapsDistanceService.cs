using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    public readonly record struct GeoPoint(double Latitude, double Longitude);

    // מסלול מלא מ-Google Routes API: Polyline מקודד, סה"כ מרחק/זמן, ומרחק/זמן לכל קטע
    public sealed record EncodedRoute(
        string? Polyline,
        double DistanceKm,
        double DurationMinutes,
        IReadOnlyList<(double DistanceKm, double DurationMinutes)> Legs);

    // מרחק וזמן נסיעה בין שתי נקודות. התוצאות נשמרות במטמון לפי הקואורדינטות,
    // וכשאין Google (אין מפתח / תקלה) מוחזרת הערכה לפי קו אווירי.
    public interface IGoogleMapsDistanceService
    {
        Task<(double DistanceKm, double DurationMinutes)> GetDrivingDistanceAsync(
            double originLat, double originLng,
            double destLat, double destLng);

        // מסלול מלא עם תחנות ביניים (לשמירת Polyline במסלול המתנדב). null אם Google לא זמין.
        Task<EncodedRoute?> GetRouteAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination);
    }

    // הפנייה הישירה ל-Google (בלי מטמון ובלי הערכות). מחזיר null כש-Google לא זמין.
    public interface IGoogleRoutesClient
    {
        Task<(double DistanceKm, double DurationMinutes)?> TryGetLegAsync(GeoPoint origin, GeoPoint destination);
        Task<EncodedRoute?> TryGetRouteAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination);
    }
}
