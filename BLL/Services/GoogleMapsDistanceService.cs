using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    // פנייה ל-Google Routes API (computeRoutes). מחזיר null בכל תקלה - ההחלטה מה לעשות בלי Google
    // נמצאת ב-GoogleMapsDistanceService.
    public sealed class GoogleRoutesClient : IGoogleRoutesClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleRoutesClient> _logger;

        public GoogleRoutesClient(HttpClient httpClient, IConfiguration configuration, ILogger<GoogleRoutesClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<(double DistanceKm, double DurationMinutes)?> TryGetLegAsync(GeoPoint origin, GeoPoint destination)
        {
            var route = await ComputeAsync(origin, Array.Empty<GeoPoint>(), destination, "routes.distanceMeters,routes.duration");
            if (route == null || route.DistanceMeters <= 0)
                return null;

            var minutes = ParseDurationMinutes(route.Duration);
            return minutes > 0 ? (route.DistanceMeters / 1000.0, minutes) : null;
        }

        public async Task<EncodedRoute?> TryGetRouteAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination)
        {
            var route = await ComputeAsync(origin, intermediates, destination,
                "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.distanceMeters,routes.legs.duration");
            if (route == null)
                return null;

            var legs = (route.Legs ?? new List<GoogleLeg>())
                .Select(l => (l.DistanceMeters / 1000.0, ParseDurationMinutes(l.Duration)))
                .ToList();

            return new EncodedRoute(route.Polyline?.EncodedPolyline, route.DistanceMeters / 1000.0, ParseDurationMinutes(route.Duration), legs);
        }

        private async Task<GoogleRoute?> ComputeAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination, string fieldMask)
        {
            var apiKey = _configuration["GoogleMaps:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return null;

            static object Waypoint(GeoPoint p) => new { location = new { latLng = new { latitude = p.Latitude, longitude = p.Longitude } } };

            var requestBody = new Dictionary<string, object>
            {
                ["origin"] = Waypoint(origin),
                ["destination"] = Waypoint(destination),
                ["travelMode"] = "DRIVE",
                ["routingPreference"] = "TRAFFIC_AWARE",
                ["computeAlternativeRoutes"] = false,
                ["languageCode"] = "he",
                ["units"] = "METRIC"
            };
            if (intermediates.Count > 0)
                requestBody["intermediates"] = intermediates.Select(Waypoint).ToArray();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes");
                request.Headers.Add("X-Goog-Api-Key", apiKey);
                request.Headers.Add("X-Goog-FieldMask", fieldMask);
                request.Content = JsonContent.Create(requestBody);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google Routes API returned HTTP {StatusCode}.", (int)response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<GoogleRoutesResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return parsed?.Routes?.FirstOrDefault();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                _logger.LogWarning(ex, "Google Routes API call failed.");
                return null;
            }
        }

        // Google מחזיר משך בפורמט protobuf, למשל "123.45s"
        private static double ParseDurationMinutes(string? duration)
        {
            if (string.IsNullOrWhiteSpace(duration) || !duration.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return 0;

            return double.TryParse(duration[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds / 60.0
                : 0;
        }

        private sealed class GoogleRoutesResponse { public List<GoogleRoute>? Routes { get; set; } }

        private sealed class GoogleRoute
        {
            public int DistanceMeters { get; set; }
            public string? Duration { get; set; }
            public GooglePolyline? Polyline { get; set; }
            public List<GoogleLeg>? Legs { get; set; }
        }

        private sealed class GoogleLeg
        {
            public int DistanceMeters { get; set; }
            public string? Duration { get; set; }
        }

        private sealed class GooglePolyline { public string? EncodedPolyline { get; set; } }
    }

    // מרחקים עם מטמון. תוקן: המנוע רץ כל 2 דקות והמטמון חי 2 דקות, כך שכמעט כל ריצה פנתה שוב ל-Google.
    // עכשיו: המפתח הוא הקואורדינטות (מעוגלות ל-~10 מ', ולא מזהי מתנדב/תרומה), תוצאה מ-Google נשמרת
    // MatchingPolicy:DistanceCacheMinutes (ברירת מחדל 3 שעות), והערכת קו-אווירי נשמרת רק 5 דקות.
    // אחרי תקלה ב-Google לא פונים אליו שוב במשך 2 דקות (כדי לא לחכות ל-Timeout בכל קריאה).
    public sealed class GoogleMapsDistanceService : IGoogleMapsDistanceService
    {
        private static readonly TimeSpan EstimateTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan BreakerDuration = TimeSpan.FromMinutes(2);

        private readonly IGoogleRoutesClient _client;
        private readonly RouteLegCache _cache;
        private readonly IMatchingPolicyProvider _policy;

        public GoogleMapsDistanceService(IGoogleRoutesClient client, RouteLegCache cache, IMatchingPolicyProvider policy)
        {
            _client = client;
            _cache = cache;
            _policy = policy;
        }

        public async Task<(double DistanceKm, double DurationMinutes)> GetDrivingDistanceAsync(
            double originLat, double originLng, double destLat, double destLng)
        {
            var policy = _policy.Current;
            var key = RouteLegCache.Key(originLat, originLng, destLat, destLng);

            if (_cache.TryGet(key, out var cached))
                return cached;

            if (!_cache.IsGoogleSuspended)
            {
                var fromGoogle = await _client.TryGetLegAsync(new GeoPoint(originLat, originLng), new GeoPoint(destLat, destLng));
                if (fromGoogle.HasValue)
                {
                    _cache.Set(key, fromGoogle.Value, TimeSpan.FromMinutes(Math.Max(1, policy.DistanceCacheMinutes)));
                    return fromGoogle.Value;
                }

                _cache.SuspendGoogle(BreakerDuration);
            }

            var estimate = Estimate(originLat, originLng, destLat, destLng, policy);
            _cache.Set(key, estimate, EstimateTtl);
            return estimate;
        }

        public async Task<EncodedRoute?> GetRouteAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination)
        {
            if (_cache.IsGoogleSuspended)
                return null;

            var route = await _client.TryGetRouteAsync(origin, intermediates, destination);
            if (route == null)
                _cache.SuspendGoogle(BreakerDuration);
            return route;
        }

        public static (double DistanceKm, double DurationMinutes) Estimate(double lat1, double lng1, double lat2, double lng2, MatchingPolicyOptions policy)
        {
            var straight = GeoUtils.HaversineKm(lat1, lng1, lat2, lng2);
            var detour = Math.Max(policy.RoadDetourFactor, 1.0);
            return (straight * detour, GeoUtils.EstimateDriveMinutes(straight, policy.AverageSpeedKmH, detour));
        }
    }

    // מטמון משותף לכל הבקשות (Singleton)
    public sealed class RouteLegCache
    {
        private const int SweepThreshold = 20_000;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double, double, DateTime)> _entries = new();
        private long _googleSuspendedUntilTicks;

        public static string Key(double lat1, double lng1, double lat2, double lng2) =>
            string.Create(CultureInfo.InvariantCulture, $"{Math.Round(lat1, 4)}|{Math.Round(lng1, 4)}|{Math.Round(lat2, 4)}|{Math.Round(lng2, 4)}");

        public bool TryGet(string key, out (double DistanceKm, double DurationMinutes) value)
        {
            if (_entries.TryGetValue(key, out var e) && e.Item3 > DateTime.UtcNow)
            {
                value = (e.Item1, e.Item2);
                return true;
            }

            value = default;
            return false;
        }

        public void Set(string key, (double DistanceKm, double DurationMinutes) value, TimeSpan ttl)
        {
            if (_entries.Count > SweepThreshold)
            {
                var now = DateTime.UtcNow;
                foreach (var kv in _entries)
                    if (kv.Value.Item3 <= now) _entries.TryRemove(kv.Key, out _);
            }

            _entries[key] = (value.DistanceKm, value.DurationMinutes, DateTime.UtcNow.Add(ttl));
        }

        public bool IsGoogleSuspended => DateTime.UtcNow.Ticks < Interlocked.Read(ref _googleSuspendedUntilTicks);

        public void SuspendGoogle(TimeSpan duration) =>
            Interlocked.Exchange(ref _googleSuspendedUntilTicks, DateTime.UtcNow.Add(duration).Ticks);

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();
    }
}
