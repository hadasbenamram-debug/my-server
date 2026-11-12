using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BLL.Notifications
{
    // תיקון 65: אימות מספר טלפון דרך Twilio Verify.
    // כאן Twilio יוצרת את הקוד, שולחת אותו בעצמה (מסרון או שיחה קולית שמקריאה אותו) ובודקת אותו -
    // ולכן זה עובד גם בחשבון ניסיון חינמי של Twilio (שבו אי אפשר לשלוח מסרון עם טקסט משלנו).
    // מופעל כש-Sms:Provider = "Twilio" ויש Sms:AccountSid, Sms:AuthToken ו-Sms:VerifyServiceSid (מתחיל ב-VA).
    // בלי Sms:VerifyServiceSid - האימות ממשיך לעבוד כמו קודם (קוד שלנו, שנשלח דרך ISmsSender).

    public enum PhoneVerifyStart
    {
        Sent,
        Failed,
        // Twilio: המספר לא יכול לקבל מסרונים (למשל קו נייח) - אפשר לבקש שיחה קולית
        CannotReceiveSms
    }

    public enum PhoneVerifyCheck
    {
        Approved,
        WrongCode,
        // אין אימות פתוח למספר: פג תוקף (10 דקות), כבר אושר, או שלא נשלח קוד
        ExpiredOrNotFound,
        TooManyAttempts,
        Failed
    }

    public interface IPhoneVerifier
    {
        bool IsConfigured { get; }

        Task<PhoneVerifyStart> StartAsync(string phoneNumber, bool byCall);

        Task<PhoneVerifyCheck> CheckAsync(string phoneNumber, string code);
    }

    public sealed class TwilioVerifyClient : IPhoneVerifier
    {
        private const string BaseUrl = "https://verify.twilio.com/v2/Services/";

        // קודי שגיאה של Twilio Verify (twilio.com/docs/api/errors)
        private const int LandlineError = 60205;          // SMS is not supported by landline phone number
        private const int MaxCheckAttemptsError = 60202;  // Max check attempts reached

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwilioVerifyClient> _logger;

        public TwilioVerifyClient(HttpClient httpClient, IConfiguration configuration, ILogger<TwilioVerifyClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsConfigured => IsConfiguredIn(_configuration);

        public static bool IsConfiguredIn(IConfiguration configuration) =>
            string.Equals(configuration["Sms:Provider"]?.Trim(), "Twilio", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(configuration["Sms:AccountSid"]) &&
            !string.IsNullOrWhiteSpace(configuration["Sms:AuthToken"]) &&
            !string.IsNullOrWhiteSpace(configuration["Sms:VerifyServiceSid"]);

        // POST .../Verifications - Twilio שולחת קוד חדש (Channel = sms או call)
        public async Task<PhoneVerifyStart> StartAsync(string phoneNumber, bool byCall)
        {
            var form = new Dictionary<string, string>
            {
                ["To"] = SmsSender.ToE164(phoneNumber),
                ["Channel"] = byCall ? "call" : "sms"
            };

            // שפת המסרון ("he" = עברית). בלי הגדרה - אנגלית (ברירת המחדל של Twilio לישראל).
            // רק למסרון: בשיחה קולית Verify לא תומכת בעברית, והקוד מוקרא באנגלית.
            // בחשבון ניסיון אפשר לשלוח רק To ו-Channel - לכן ברירת המחדל ריקה.
            var locale = _configuration["Sms:VerifyLocale"];
            if (!byCall && !string.IsNullOrWhiteSpace(locale))
                form["Locale"] = locale.Trim();

            try
            {
                using var response = await PostAsync("Verifications", form);
                if (response.IsSuccessStatusCode)
                    return PhoneVerifyStart.Sent;

                var (code, message) = await ReadErrorAsync(response);
                _logger.LogWarning("Twilio Verify did not send the code ({Channel}): HTTP {Status}, error {Code}: {Message}",
                    form["Channel"], (int)response.StatusCode, code, message);
                return code == LandlineError ? PhoneVerifyStart.CannotReceiveSms : PhoneVerifyStart.Failed;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                _logger.LogWarning(ex, "Twilio Verify is unreachable (start).");
                return PhoneVerifyStart.Failed;
            }
        }

        // POST .../VerificationCheck - status "approved" = הקוד נכון
        public async Task<PhoneVerifyCheck> CheckAsync(string phoneNumber, string code)
        {
            var form = new Dictionary<string, string>
            {
                ["To"] = SmsSender.ToE164(phoneNumber),
                ["Code"] = code
            };

            try
            {
                using var response = await PostAsync("VerificationCheck", form);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return PhoneVerifyCheck.ExpiredOrNotFound;

                if (!response.IsSuccessStatusCode)
                {
                    var (errorCode, message) = await ReadErrorAsync(response);
                    if (errorCode == MaxCheckAttemptsError)
                        return PhoneVerifyCheck.TooManyAttempts;

                    _logger.LogWarning("Twilio Verify check failed: HTTP {Status}, error {Code}: {Message}",
                        (int)response.StatusCode, errorCode, message);
                    return PhoneVerifyCheck.Failed;
                }

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var status = json.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                return string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase)
                    ? PhoneVerifyCheck.Approved
                    : PhoneVerifyCheck.WrongCode;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                _logger.LogWarning(ex, "Twilio Verify is unreachable (check).");
                return PhoneVerifyCheck.Failed;
            }
        }

        private async Task<HttpResponseMessage> PostAsync(string action, Dictionary<string, string> form)
        {
            var sid = _configuration["Sms:AccountSid"]!;
            var token = _configuration["Sms:AuthToken"]!;
            var serviceSid = _configuration["Sms:VerifyServiceSid"]!.Trim();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Uri.EscapeDataString(serviceSid)}/{action}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            request.Content = new FormUrlEncodedContent(form);

            return await _httpClient.SendAsync(request);
        }

        // גוף שגיאה של Twilio: {"code": 60200, "message": "...", "status": 400, "more_info": "..."}
        private static async Task<(int? Code, string Message)> ReadErrorAsync(HttpResponseMessage response)
        {
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = json.RootElement;
                int? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                return (code, message);
            }
            catch (JsonException)
            {
                return (null, "");
            }
        }
    }
}
