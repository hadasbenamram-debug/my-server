using System.Threading;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    // המיקום שנמצא לכתובת: קו רוחב, קו אורך, הכתובת כפי ש-Google מכיר אותה, ומזהה המקום ב-Google
    public sealed record GeocodeResult(double Latitude, double Longitude, string FormattedAddress, string PlaceId);

    // Service עזר: כתובת -> מיקום מדויק (Google Geocoding API).
    // הפנייה ל-Google נעשית רק מהשרת, והמפתח לא יוצא ממנו. הלקוח שולח רק כתובת.
    public interface IGeocodingService
    {
        // כתובת שלא נמצאה או כללית מדי - ArgumentException (400 עם הודעה ברורה).
        // Google לא זמין, או שהמפתח חסר / נדחה - ServiceUnavailableException (503).
        Task<GeocodeResult> GeocodeAsync(string address, CancellationToken cancellationToken = default);
    }
}
