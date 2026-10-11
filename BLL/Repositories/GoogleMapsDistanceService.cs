using BLL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BLL.Integrations.GoogleMaps
{
    public class GoogleMapsDistanceService : IGoogleMapsDistanceService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleMapsDistanceService> _logger;

        public GoogleMapsDistanceService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GoogleMapsDistanceService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<(double DistanceKm, double DurationMinutes)> GetDrivingDistanceAsync(
            double originLat,
            double originLng,
            double destLat,
            double destLng)
        {
            try
            {
                var apiKey = _configuration["GoogleMaps:ApiKey"];

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning(
                        "Google Maps API key is not configured. Using Haversine fallback.");

                    return CalculateHaversineFallback(
                        originLat,
                        originLng,
                        destLat,
                        destLng);
                }

                var requestBody = new
                {
                    origin = new
                    {
                        location = new
                        {
                            latLng = new
                            {
                                latitude = originLat,
                                longitude = originLng
                            }
                        }
                    },

                    destination = new
                    {
                        location = new
                        {
                            latLng = new
                            {
                                latitude = destLat,
                                longitude = destLng
                            }
                        }
                    },

                    travelMode = "DRIVE",

                    routingPreference = "TRAFFIC_AWARE",

                    computeAlternativeRoutes = false,

                    languageCode = "en-US",

                    units = "METRIC"
                };

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "directions/v2:computeRoutes");

                request.Headers.Add("X-Goog-Api-Key", apiKey);

                request.Headers.Add(
                    "X-Goog-FieldMask",
                    "routes.distanceMeters,routes.duration");

                request.Content = JsonContent.Create(requestBody);

                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();

                    _logger.LogWarning(
                        "Google Maps API returned HTTP {StatusCode}. Response: {Response}. Using Haversine fallback.",
                        response.StatusCode,
                        errorBody);

                    return CalculateHaversineFallback(
                        originLat,
                        originLng,
                        destLat,
                        destLng);
                }

                var json = await response.Content.ReadAsStringAsync();

                var googleResponse =
                    JsonSerializer.Deserialize<GoogleRoutesResponse>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                var route = googleResponse?.Routes?.FirstOrDefault();

                if (route == null)
                {
                    _logger.LogWarning(
                        "Google Maps returned no route. Using Haversine fallback.");

                    return CalculateHaversineFallback(
                        originLat,
                        originLng,
                        destLat,
                        destLng);
                }

                var distanceKm = route.DistanceMeters / 1000.0;

                var durationMinutes =
                    ParseGoogleDurationToMinutes(route.Duration);

                if (distanceKm <= 0 || durationMinutes <= 0)
                {
                    _logger.LogWarning(
                        "Google Maps returned invalid distance/duration. Using Haversine fallback.");

                    return CalculateHaversineFallback(
                        originLat,
                        originLng,
                        destLat,
                        destLng);
                }

                _logger.LogDebug(
                    "Google Maps route calculated successfully. Distance: {DistanceKm} km, Duration: {DurationMinutes} minutes.",
                    distanceKm,
                    durationMinutes);

                return (distanceKm, durationMinutes);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Network error while calling Google Maps. Using Haversine fallback.");

                return CalculateHaversineFallback(
                    originLat,
                    originLng,
                    destLat,
                    destLng);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Google Maps request timed out. Using Haversine fallback.");

                return CalculateHaversineFallback(
                    originLat,
                    originLng,
                    destLat,
                    destLng);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse Google Maps response. Using Haversine fallback.");

                return CalculateHaversineFallback(
                    originLat,
                    originLng,
                    destLat,
                    destLng);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while calling Google Maps. Using Haversine fallback.");

                return CalculateHaversineFallback(
                    originLat,
                    originLng,
                    destLat,
                    destLng);
            }
        }

        private static double ParseGoogleDurationToMinutes(string? duration)
        {
            if (string.IsNullOrWhiteSpace(duration))
                return 0;

            // Google returns duration in protobuf format, e.g.
            // "123.45s"

            if (!duration.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return 0;

            var numericPart = duration[..^1];

            if (!double.TryParse(
                    numericPart,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds))
            {
                return 0;
            }

            return seconds / 60.0;
        }

        private static (double DistanceKm, double DurationMinutes)
            CalculateHaversineFallback(
                double originLat,
                double originLng,
                double destLat,
                double destLng)
        {
            const double earthRadiusKm = 6371.0;

            static double ToRad(double degrees)
                => degrees * Math.PI / 180.0;

            var dLat = ToRad(destLat - originLat);
            var dLon = ToRad(destLng - originLng);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(originLat)) *
                Math.Cos(ToRad(destLat)) *
                Math.Sin(dLon / 2) *
                Math.Sin(dLon / 2);

            var c =
                2 * Math.Atan2(
                    Math.Sqrt(a),
                    Math.Sqrt(1 - a));

            var distanceKm = earthRadiusKm * c;

            // Fallback בלבד.
            // זה אינו זמן נסיעה אמיתי.
            const double fallbackAverageSpeedKmH = 40.0;

            var durationMinutes =
                distanceKm / fallbackAverageSpeedKmH * 60.0;

            return (distanceKm, durationMinutes);
        }

        private sealed class GoogleRoutesResponse
        {
            public List<GoogleRoute>? Routes { get; set; }
        }

        private sealed class GoogleRoute
        {
            public int DistanceMeters { get; set; }

            public string? Duration { get; set; }
        }
    }
}