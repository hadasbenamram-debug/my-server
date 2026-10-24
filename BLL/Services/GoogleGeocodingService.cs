using BLL.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    // Service עזר: הופך כתובת לנקודה מדויקת על המפה בעזרת Google Geocoding API.
    // המפתח (GoogleMaps:ApiKey) נקרא מההגדרות של השרת - User Secrets / משתנה סביבה - ולא נשלח ללקוח.
    public sealed class GoogleGeocodingService : IGeocodingService
    {
        public const string AddressNotFoundMessage =
            "הכתובת לא נמצאה במפה. יש לכתוב כתובת מלאה: רחוב, מספר בית ועיר.";

        public const string AddressTooGeneralMessage =
            "הכתובת כללית מדי. יש לכתוב כתובת מלאה: רחוב, מספר בית ועיר.";

        private const string UnavailableMessage =
            "שירות המפות אינו זמין כרגע, ולכן לא ניתן לאתר את הכתובת. נסו שוב מאוחר יותר.";

        // תוצאות רחבות מדי: מדינה או מחוז. עיר או יישוב מתקבלים, כי ביישובים קטנים אין תמיד רחובות.
        private static readonly HashSet<string> TooGeneralTypes = new(StringComparer.Ordinal)
        {
            "country",
            "administrative_area_level_1",
            "administrative_area_level_2"
        };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleGeocodingService> _logger;

        public GoogleGeocodingService(HttpClient httpClient, IConfiguration configuration, ILogger<GoogleGeocodingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<GeocodeResult> GeocodeAsync(string address, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException(AddressNotFoundMessage);

            var apiKey = _configuration["GoogleMaps:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ServiceUnavailableException(UnavailableMessage);

            // components=country:IL - רק תוצאות בישראל. language / region - תוצאות בעברית, מותאמות לישראל.
            var requestUri = "geocode/json?address=" + Uri.EscapeDataString(address.Trim()) +
                             "&components=country:IL&language=iw&region=il" +
                             "&key=" + Uri.EscapeDataString(apiKey.Trim());

            GeocodeResponse? body;
            try
            {
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // לא רושמים בלוג את כתובת הבקשה - יש בה את המפתח
                    _logger.LogWarning("Google Geocoding API returned HTTP {StatusCode}.", (int)response.StatusCode);
                    throw new ServiceUnavailableException(UnavailableMessage);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                body = JsonSerializer.Deserialize<GeocodeResponse>(json);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException ||
                                       (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogWarning(ex, "Google Geocoding API call failed.");
                throw new ServiceUnavailableException(UnavailableMessage);
            }

            var status = body?.Status ?? "UNKNOWN_ERROR";
            if (status == "ZERO_RESULTS")
                throw new ArgumentException(AddressNotFoundMessage);

            if (status != "OK")
            {
                // למשל REQUEST_DENIED: המפתח שגוי או חסום, או ש-Geocoding API לא מופעל בפרויקט ב-Google Cloud
                _logger.LogWarning("Google Geocoding API returned {Status}: {ErrorMessage}", status, body?.ErrorMessage);
                throw new ServiceUnavailableException(UnavailableMessage);
            }

            var result = body?.Results?.FirstOrDefault();
            var location = result?.Geometry?.Location;
            if (result == null || location == null)
                throw new ArgumentException(AddressNotFoundMessage);

            // Google לא זיהה את הכתובת במלואה והחזיר רק אזור כללי - זה לא מיקום מדויק
            bool approximate = result.Geometry!.LocationType == "APPROXIMATE";
            if (approximate && result.PartialMatch)
                throw new ArgumentException(AddressNotFoundMessage);

            if (approximate && (result.Types ?? new List<string>()).Any(TooGeneralTypes.Contains))
                throw new ArgumentException(AddressTooGeneralMessage);

            // העמודה GooglePlaceId במסד מקבלת עד 255 תווים
            var placeId = string.IsNullOrWhiteSpace(result.PlaceId) || result.PlaceId.Length > 255 ? "N/A" : result.PlaceId;

            return new GeocodeResult(location.Lat, location.Lng, result.FormattedAddress ?? address.Trim(), placeId);
        }

        // מבנה התשובה של Google (רק השדות שבשימוש)
        private sealed class GeocodeResponse
        {
            [JsonPropertyName("status")] public string? Status { get; set; }
            [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
            [JsonPropertyName("results")] public List<GeocodeItem>? Results { get; set; }
        }

        private sealed class GeocodeItem
        {
            [JsonPropertyName("formatted_address")] public string? FormattedAddress { get; set; }
            [JsonPropertyName("place_id")] public string? PlaceId { get; set; }
            [JsonPropertyName("partial_match")] public bool PartialMatch { get; set; }
            [JsonPropertyName("types")] public List<string>? Types { get; set; }
            [JsonPropertyName("geometry")] public GeocodeGeometry? Geometry { get; set; }
        }

        private sealed class GeocodeGeometry
        {
            [JsonPropertyName("location")] public GeocodeLocation? Location { get; set; }
            [JsonPropertyName("location_type")] public string? LocationType { get; set; }
        }

        private sealed class GeocodeLocation
        {
            [JsonPropertyName("lat")] public double Lat { get; set; }
            [JsonPropertyName("lng")] public double Lng { get; set; }
        }
    }
}
