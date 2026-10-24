using BLL.Exceptions;
using BLL.Integrations.GoogleMaps;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // שירותי Google Maps של השרת, בלי פנייה אמיתית ל-Google: התשובה מגיעה מ-HttpMessageHandler מזויף.
    public class GoogleMapsServicesTests
    {
        private const string GoogleBaseUrl = "https://maps.googleapis.com/maps/api/";

        // מחזיר תמיד את אותה תשובה, ושומר את הבקשה האחרונה
        private sealed class FakeGoogle : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            private readonly string _mediaType;

            public FakeGoogle(HttpStatusCode status, string body, string mediaType = "application/json")
            {
                _status = status;
                _body = body;
                _mediaType = mediaType;
            }

            public Uri? LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, _mediaType)
                });
            }
        }

        private static GoogleGeocodingService Geocoder(FakeGoogle google, string key = "test-key") =>
            new(new HttpClient(google) { BaseAddress = new Uri(GoogleBaseUrl) },
                TestDb.Config(new Dictionary<string, string?> { ["GoogleMaps:ApiKey"] = key }),
                TestDb.Log<GoogleGeocodingService>());

        private static string OneResult(string locationType, bool partialMatch = false, string types = "\"street_address\"") =>
            "{\"status\":\"OK\",\"results\":[{\"formatted_address\":\"הרצל 10, תל אביב-יפו, ישראל\",\"place_id\":\"place-1\"," +
            "\"partial_match\":" + (partialMatch ? "true" : "false") + ",\"types\":[" + types + "]," +
            "\"geometry\":{\"location\":{\"lat\":32.0615,\"lng\":34.7713},\"location_type\":\"" + locationType + "\"}}]}";

        // ---------------- Geocoding: כתובת -> מיקום ----------------

        [Fact]
        public async Task Geocode_ExactAddress_ReturnsThePoint_AndTheKeyGoesOnlyToGoogle()
        {
            var google = new FakeGoogle(HttpStatusCode.OK, OneResult("ROOFTOP"));

            var result = await Geocoder(google).GeocodeAsync("הרצל 10, תל אביב");

            Assert.Equal(32.0615, result.Latitude, 4);
            Assert.Equal(34.7713, result.Longitude, 4);
            Assert.Equal("place-1", result.PlaceId);

            var request = google.LastRequestUri!;
            Assert.Equal("maps.googleapis.com", request.Host);
            Assert.Contains("key=test-key", request.Query);
        }

        [Fact]
        public async Task Geocode_AddressNotFound_IsBadRequest()
        {
            var google = new FakeGoogle(HttpStatusCode.OK, "{\"status\":\"ZERO_RESULTS\",\"results\":[]}");

            await Assert.ThrowsAsync<ArgumentException>(() => Geocoder(google).GeocodeAsync("כתובת שלא קיימת"));
        }

        [Fact]
        public async Task Geocode_PartialMatchOfAWholeArea_IsNotAnExactLocation()
        {
            var google = new FakeGoogle(HttpStatusCode.OK,
                OneResult("APPROXIMATE", partialMatch: true, types: "\"locality\",\"political\""));

            await Assert.ThrowsAsync<ArgumentException>(() => Geocoder(google).GeocodeAsync("רחוב שלא קיים 5, תל אביב"));
        }

        [Fact]
        public async Task Geocode_OnlyTheCountry_IsTooGeneral()
        {
            var google = new FakeGoogle(HttpStatusCode.OK,
                OneResult("APPROXIMATE", types: "\"country\",\"political\""));

            await Assert.ThrowsAsync<ArgumentException>(() => Geocoder(google).GeocodeAsync("ישראל"));
        }

        [Fact]
        public async Task Geocode_KeyRejectedByGoogle_IsServiceUnavailable()
        {
            var google = new FakeGoogle(HttpStatusCode.OK,
                "{\"status\":\"REQUEST_DENIED\",\"error_message\":\"bad key\",\"results\":[]}");

            await Assert.ThrowsAsync<ServiceUnavailableException>(() => Geocoder(google).GeocodeAsync("הרצל 10, תל אביב"));
        }

        [Fact]
        public async Task Geocode_WithoutKey_IsServiceUnavailable_AndGoogleIsNotCalled()
        {
            var google = new FakeGoogle(HttpStatusCode.OK, "{}");

            await Assert.ThrowsAsync<ServiceUnavailableException>(() => Geocoder(google, key: "").GeocodeAsync("הרצל 10, תל אביב"));
            Assert.Null(google.LastRequestUri);
        }
    }
}
