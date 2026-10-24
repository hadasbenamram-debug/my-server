using BLL.Exceptions;
using FoodRescue.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    // Service עזר: מביא מ-Google תמונת מפה (Maps Static API) עם סיכות וקו מסלול.
    // השרת פונה ל-Google עם המפתח ומחזיר ללקוח רק את התמונה, כך שהמפתח לא מגיע לדפדפן.
    public sealed class GoogleStaticMapService : IStaticMapService
    {
        // מגבלות של Google: תמונה עד 640x640 (scale=2 מכפיל את הרזולוציה), וכתובת בקשה עד 16,384 תווים
        private const string ImageSize = "640x420";
        private const int MaxPins = 200;
        private const string RouteColor = "0x159aa0ff";
        private const string DefaultPinColor = "8a8f91";

        private const string UnavailableMessage = "לא ניתן להציג את המפה כרגע. נסו שוב מאוחר יותר.";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleStaticMapService> _logger;

        public GoogleStaticMapService(HttpClient httpClient, IConfiguration configuration, ILogger<GoogleStaticMapService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MapImageDto> RenderAsync(
            IReadOnlyList<MapPin> pins,
            string? encodedPolyline = null,
            bool connectPins = false,
            CancellationToken cancellationToken = default)
        {
            var apiKey = _configuration["GoogleMaps:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ServiceUnavailableException(UnavailableMessage);

            var requestUri = BuildRequestPath(pins, encodedPolyline, connectPins) +
                             "&key=" + Uri.EscapeDataString(apiKey.Trim());

            try
            {
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                if (!response.IsSuccessStatusCode || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    // Google כותב בתשובה למה הבקשה נדחתה (למשל: השירות לא הופעל, או שהמפתח מוגבל).
                    // הסיבה נרשמת בלוג של השרת בלבד. את כתובת הבקשה לא רושמים - יש בה את המפתח.
                    var reason = await ReadGoogleReasonAsync(response, mediaType, apiKey, cancellationToken);
                    _logger.LogWarning("Google Maps Static API returned HTTP {StatusCode} ({MediaType}): {Reason}",
                        (int)response.StatusCode, mediaType, reason);
                    throw new ServiceUnavailableException(UnavailableMessage);
                }

                return new MapImageDto
                {
                    Content = await response.Content.ReadAsByteArrayAsync(cancellationToken),
                    ContentType = mediaType
                };
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                                       (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogWarning(ex, "Google Maps Static API call failed.");
                throw new ServiceUnavailableException(UnavailableMessage);
            }
        }

        // הסיבה ש-Google נתן לשגיאה: טקסט התשובה (עד 500 תווים) והכותרת X-Staticmap-API-Warning.
        // המפתח מוחלף ב-*** ליתר ביטחון, כדי שלא יגיע ללוג גם אם Google מחזיר אותו.
        private static async Task<string> ReadGoogleReasonAsync(
            HttpResponseMessage response, string mediaType, string apiKey, CancellationToken cancellationToken)
        {
            var parts = new List<string>();

            if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                // לפי Google: תמונת שגיאה במקום מפה - בדרך כלל אין חיוב (Billing) בפרויקט, או שהמפתח לא תקין
                parts.Add("Google returned an error image (usually: billing is not enabled on the project, or the key is not valid).");
            }
            else
            {
                var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                if (text.Length > 500)
                    text = text.Substring(0, 500);
                if (text.Length > 0)
                    parts.Add(text);
            }

            if (response.Headers.TryGetValues("X-Staticmap-API-Warning", out var warnings))
                parts.AddRange(warnings);

            var reason = parts.Count > 0 ? string.Join(" | ", parts) : "(no details from Google)";
            return reason.Replace(apiKey.Trim(), "***", StringComparison.Ordinal);
        }

        // כל חלקי הבקשה ל-Google חוץ מהמפתח (ציבורי כדי שאפשר יהיה לבדוק אותו בטסט)
        public static string BuildRequestPath(IReadOnlyList<MapPin> pins, string? encodedPolyline, bool connectPins)
        {
            var shown = pins.Where(p => HasLocation(p.Latitude, p.Longitude)).Take(MaxPins).ToList();
            bool hasRoute = !string.IsNullOrWhiteSpace(encodedPolyline);

            var request = new StringBuilder("staticmap?size=" + ImageSize + "&scale=2&maptype=roadmap&language=iw&region=il");

            // סיכות עם אותו צבע ואותה תווית נשלחות יחד (הבקשה קצרה יותר)
            foreach (var group in shown.GroupBy(p => (Color: NormalizeColor(p.ColorHex), p.Label)))
            {
                var style = "color:0x" + group.Key.Color;
                if (group.Key.Label is char label)
                    style += "|label:" + char.ToUpperInvariant(label);

                request.Append("&markers=").Append(Uri.EscapeDataString(style + "|" + string.Join("|", group.Select(Point))));
            }

            if (hasRoute)
                request.Append("&path=").Append(Uri.EscapeDataString("color:" + RouteColor + "|weight:5|enc:" + encodedPolyline!.Trim()));
            else if (connectPins && shown.Count > 1)
                request.Append("&path=").Append(Uri.EscapeDataString("color:" + RouteColor + "|weight:4|" + string.Join("|", shown.Select(Point))));

            // בלי סיכות ובלי קו Google לא יודע מה להציג - מרכז הארץ. סיכה אחת - זום של רחוב.
            if (shown.Count == 0 && !hasRoute)
                request.Append("&center=31.5,34.9&zoom=7");
            else if (shown.Count == 1 && !hasRoute)
                request.Append("&center=").Append(Uri.EscapeDataString(Point(shown[0]))).Append("&zoom=15");

            return request.ToString();
        }

        // 0,0 במערכת = "אין מיקום" (למשל מתנדב שעוד לא שיתף מיקום)
        public static bool HasLocation(double latitude, double longitude) =>
            latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
            !(latitude == 0 && longitude == 0);

        private static string Point(MapPin pin) =>
            pin.Latitude.ToString("0.######", CultureInfo.InvariantCulture) + "," +
            pin.Longitude.ToString("0.######", CultureInfo.InvariantCulture);

        private static string NormalizeColor(string? color)
        {
            var hex = (color ?? string.Empty).Trim().TrimStart('#');
            return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : DefaultPinColor;
        }
    }
}
