using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    // סיכה על המפה: צבע בפורמט RRGGBB, ותווית של תו אחד (ספרה או אות לועזית) - לא חובה
    public sealed record MapPin(double Latitude, double Longitude, string ColorHex, char? Label = null);

    // Service עזר: תמונת מפה מ-Google Maps Static API. השרת מביא את התמונה עם המפתח,
    // והלקוח מקבל רק את התמונה - כך שהמפתח של Google לא מגיע לדפדפן.
    public interface IStaticMapService
    {
        // encodedPolyline - קו המסלול בכבישים (אם יש). connectPins - כשאין קו כזה, לחבר את הסיכות בקו ישר.
        Task<MapImageDto> RenderAsync(
            IReadOnlyList<MapPin> pins,
            string? encodedPolyline = null,
            bool connectPins = false,
            CancellationToken cancellationToken = default);
    }
}
