using BLL.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // שליחת SMS אמיתית: בחירת הספק לפי ההגדרות, ומבנה הבקשה ל-019 SMS (בלי רשת - HttpMessageHandler מזויף)
    public class SmsSenderTests
    {
        private sealed class Env : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Production;
            public string ApplicationName { get; set; } = "Tests";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly string _responseJson;
            public HttpRequestMessage? Request { get; private set; }
            public string? Body { get; private set; }
            public CaptureHandler(string responseJson) => _responseJson = responseJson;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseJson) };
            }
        }

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static readonly Dictionary<string, string?> Valid019 = new()
        {
            ["Sms:Provider"] = "019", ["Sms:Username"] = "latet", ["Sms:ApiToken"] = "secret-token", ["Sms:Source"] = "LatetBeahava"
        };

        [Theory]
        [InlineData("019", true, "Production", SmsMode.Israel019)]
        [InlineData("019", false, "Production", SmsMode.NotConfigured)]     // חסרים פרטים - לא "נשלח" בשקט
        [InlineData("Log", false, "Development", SmsMode.DevelopmentLog)]
        [InlineData("Log", false, "Production", SmsMode.NotConfigured)]     // בעבר: "נשלח" גם בפרודקשן
        [InlineData("", false, "Development", SmsMode.DevelopmentLog)]
        public void ResolveMode_FollowsTheConfiguration(string provider, bool withCredentials, string environment, SmsMode expected)
        {
            var values = withCredentials ? new Dictionary<string, string?>(Valid019) : new Dictionary<string, string?>();
            values["Sms:Provider"] = provider;

            Assert.Equal(expected, SmsSender.ResolveMode(Config(values), new Env { EnvironmentName = environment }));
        }

        [Theory]
        [InlineData("050-1234567", "0501234567")]
        [InlineData("+972 50 123 4567", "0501234567")]
        [InlineData("972501234567", "0501234567")]
        [InlineData("501234567", "0501234567")]
        public void ToIsraeliLocal_NormalizesThePhone(string input, string expected) =>
            Assert.Equal(expected, SmsSender.ToIsraeliLocal(input));

        [Fact]
        public async Task Send019_PostsTheDocumentedJson_WithBearerToken_AndReturnsTheShipmentId()
        {
            var handler = new CaptureHandler("{\"status\":0,\"message\":\"SMS will be sent\",\"shipment_id\":\"12345\"}");
            var sender = new SmsSender(new HttpClient(handler), Config(Valid019), new Env(), TestDb.Log<SmsSender>());

            var id = await sender.SendAsync("050-1234567", "קוד 123456");

            Assert.Equal("12345", id);
            Assert.Equal(HttpMethod.Post, handler.Request!.Method);
            Assert.Equal("https://019sms.co.il/api", handler.Request.RequestUri!.ToString());
            Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
            Assert.Equal("secret-token", handler.Request.Headers.Authorization.Parameter);

            using var json = JsonDocument.Parse(handler.Body!);
            var sms = json.RootElement.GetProperty("sms");
            Assert.Equal("latet", sms.GetProperty("user").GetProperty("username").GetString());
            Assert.Equal("LatetBeahava", sms.GetProperty("source").GetString());
            Assert.Equal("0501234567", sms.GetProperty("destinations").GetProperty("phone")[0].GetProperty("_").GetString());
            Assert.Equal("קוד 123456", sms.GetProperty("message").GetString());
        }

        [Fact]
        public async Task Send019_ErrorStatus_Throws_SoTheDispatcherRetries()
        {
            var handler = new CaptureHandler("{\"status\":3,\"message\":\"Username or password is incorrect\"}");
            var sender = new SmsSender(new HttpClient(handler), Config(Valid019), new Env(), TestDb.Log<SmsSender>());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("0501234567", "x"));
            Assert.Contains("status 3", ex.Message);
        }

        [Fact]
        public async Task NotConfigured_Throws_InsteadOfPretendingItWasSent()
        {
            var sender = new SmsSender(new HttpClient(new CaptureHandler("{}")), Config(new()), new Env(), TestDb.Log<SmsSender>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("0501234567", "x"));
        }
    }
}
