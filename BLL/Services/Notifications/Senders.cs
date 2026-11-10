using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BLL.Notifications
{
    // שליחה בפועל של מייל. מחזיר מזהה הודעה (אם יש). נקרא רק מתוך NotificationDispatcher (ברקע).
    public interface IEmailSender
    {
        Task<string?> SendAsync(string toEmail, string subject, string htmlBody);
    }

    // איך השרת שולח SMS כרגע - לפי Sms:Provider והפרטים שהוגדרו
    public enum SmsMode
    {
        NotConfigured,    // אין ספק SMS תקין - לא שולחים (ומחזירים למשתמש הודעה ברורה)
        DevelopmentLog,   // סביבת פיתוח בלי ספק: ההודעה נכתבת ביומן השרת במקום להישלח (כדי שאפשר יהיה לבדוק)
        Twilio,
        Israel019         // 019 SMS (ספק ישראלי)
    }

    // שליחה בפועל של SMS. מחזיר מזהה הודעה אצל הספק (אם יש).
    public interface ISmsSender
    {
        SmsMode Mode { get; }

        Task<string?> SendAsync(string phoneNumber, string text);
    }

    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration configuration, IHostEnvironment environment, ILogger<SmtpEmailSender> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<string?> SendAsync(string toEmail, string subject, string htmlBody)
        {
            var smtpServer = _configuration["EmailSettings:Server"];
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var senderPassword = _configuration["EmailSettings:SenderPassword"];

            if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(senderEmail))
            {
                // בסביבת פיתוח בלי SMTP - לא נכשלים, רק מתעדים בלוג (בלי גוף ההודעה)
                if (_environment.IsDevelopment())
                {
                    _logger.LogWarning("EmailSettings is not configured. Development mode: e-mail '{Subject}' to {To} was not sent.", subject, toEmail);
                    return "dev-not-sent";
                }

                throw new InvalidOperationException("EmailSettings (Server/SenderEmail) is not configured.");
            }

            var port = int.TryParse(_configuration["EmailSettings:Port"], out var parsedPort) ? parsedPort : 587;
            var messageId = $"<{Guid.NewGuid():N}@{new MailAddress(senderEmail).Host}>";

            using var client = new SmtpClient(smtpServer, port)
            {
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl = true
            };

            // תיקון 37: כל מייל נשלח בעיצוב של האתר (EmailTemplate), ובנוסף גרסת טקסט רגיל (multipart/alternative)
            var siteUrl = _configuration["App:FrontendBaseUrl"];
            var html = EmailTemplate.Wrap(subject, htmlBody, siteUrl);
            var plainText = EmailTemplate.PlainText(subject, htmlBody, siteUrl);

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, EmailTemplate.BrandName, Encoding.UTF8),
                Subject = subject,
                Body = plainText,
                IsBodyHtml = false,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
            mailMessage.To.Add(toEmail);
            mailMessage.Headers.Add("Message-ID", messageId);

            await client.SendMailAsync(mailMessage);
            return messageId;
        }
    }

    // SMS - נשלח ישירות ל-REST API של הספק, בלי חבילה חיצונית. Sms:Provider:
    //   "019"    - ספק ישראלי (019sms.co.il): Sms:Username, Sms:ApiToken, Sms:Source (שם שולח מאושר, עד 11 תווים באנגלית/ספרות)
    //   "Twilio" - Sms:AccountSid, Sms:AuthToken, Sms:From
    //   "Log"    - פיתוח בלבד: לא נשלח, ההודעה נכתבת ביומן השרת
    // תוקן: בעבר ברירת המחדל הייתה "Log" גם כשלא בפיתוח, וההודעה סומנה "נשלחה" בלי שנשלחה בפועל.
    public sealed class SmsSender : ISmsSender
    {
        private const string Default019Url = "https://019sms.co.il/api";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<SmsSender> _logger;

        public SmsSender(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment, ILogger<SmsSender> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public SmsMode Mode => ResolveMode(_configuration, _environment);

        public static SmsMode ResolveMode(IConfiguration configuration, IHostEnvironment environment)
        {
            var provider = configuration["Sms:Provider"]?.Trim();

            if (string.Equals(provider, "Twilio", StringComparison.OrdinalIgnoreCase))
                return HasAll(configuration, "Sms:AccountSid", "Sms:AuthToken", "Sms:From") ? SmsMode.Twilio : SmsMode.NotConfigured;

            if (string.Equals(provider, "019", StringComparison.OrdinalIgnoreCase))
                return HasAll(configuration, "Sms:Username", "Sms:ApiToken", "Sms:Source") ? SmsMode.Israel019 : SmsMode.NotConfigured;

            // "Log" / ריק: רק בסביבת פיתוח
            if ((string.IsNullOrEmpty(provider) || string.Equals(provider, "Log", StringComparison.OrdinalIgnoreCase)) &&
                environment.IsDevelopment())
                return SmsMode.DevelopmentLog;

            return SmsMode.NotConfigured;
        }

        public async Task<string?> SendAsync(string phoneNumber, string text)
        {
            switch (Mode)
            {
                case SmsMode.Twilio:
                    return await SendTwilioAsync(phoneNumber, text);

                case SmsMode.Israel019:
                    return await Send019Async(phoneNumber, text);

                case SmsMode.DevelopmentLog:
                    // סביבת פיתוח בלבד: ההודעה (כולל קוד אימות) נכתבת ביומן כדי שאפשר יהיה לבדוק את התהליך בלי ספק SMS
                    _logger.LogWarning("Development: SMS provider is not configured (Sms:Provider), the SMS was NOT sent. To {Phone}: {Text}",
                        Mask(phoneNumber), text);
                    return "dev-log-only";

                default:
                    throw new InvalidOperationException(
                        "SMS is not configured. Set Sms:Provider to \"019\" (Sms:Username, Sms:ApiToken, Sms:Source) or \"Twilio\" (Sms:AccountSid, Sms:AuthToken, Sms:From).");
            }
        }

        private async Task<string?> SendTwilioAsync(string phoneNumber, string text)
        {
            var sid = _configuration["Sms:AccountSid"]!;
            var token = _configuration["Sms:AuthToken"]!;
            var from = _configuration["Sms:From"]!;

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = ToE164(phoneNumber),
                ["From"] = from,
                ["Body"] = text
            });

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"SMS provider returned HTTP {(int)response.StatusCode}.");

            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
        }

        // 019 SMS: POST https://019sms.co.il/api עם Authorization: Bearer <token> וגוף JSON (docs.019sms.co.il).
        // status 0 = ההודעה תישלח; כל ערך אחר = שגיאה (למשל 3 טוקן שגוי, 4 אין יתרה, 515 שם שולח לא מאושר).
        private async Task<string?> Send019Async(string phoneNumber, string text)
        {
            var url = _configuration["Sms:Url"];
            var payload = new
            {
                sms = new
                {
                    user = new { username = _configuration["Sms:Username"] },
                    source = _configuration["Sms:Source"],
                    destinations = new { phone = new[] { new Dictionary<string, string> { ["_"] = ToIsraeliLocal(phoneNumber) } } },
                    message = text
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, string.IsNullOrWhiteSpace(url) ? Default019Url : url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["Sms:ApiToken"]);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"019 SMS returned HTTP {(int)response.StatusCode}.");

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            int status = -1;
            if (root.TryGetProperty("status", out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.Number) status = statusProp.GetInt32();
                else if (statusProp.ValueKind == JsonValueKind.String && int.TryParse(statusProp.GetString(), out var parsed)) status = parsed;
            }

            if (status != 0)
            {
                var message = root.TryGetProperty("message", out var m) ? m.ToString() : "";
                throw new InvalidOperationException($"019 SMS returned status {status}: {message}");
            }

            return root.TryGetProperty("shipment_id", out var shipment) ? shipment.ToString() : null;
        }

        private static bool HasAll(IConfiguration configuration, params string[] keys)
        {
            foreach (var key in keys)
                if (string.IsNullOrWhiteSpace(configuration[key])) return false;
            return true;
        }

        // מספר ישראלי בפורמט ש-019 מקבל: 05XXXXXXXX (גם +972 / 972 מומרים)
        public static string ToIsraeliLocal(string phone)
        {
            var digits = new StringBuilder();
            foreach (var c in phone ?? string.Empty)
                if (char.IsAsciiDigit(c)) digits.Append(c);

            var s = digits.ToString();
            if (s.StartsWith("972")) return "0" + s.Substring(3);
            return s.StartsWith("0") ? s : "0" + s;
        }

        // מספר ישראלי 05X... -> +9725X...
        public static string ToE164(string phone)
        {
            var digits = new StringBuilder();
            foreach (var c in phone ?? string.Empty)
                if (char.IsAsciiDigit(c) || (c == '+' && digits.Length == 0)) digits.Append(c);

            var s = digits.ToString();
            if (s.StartsWith("+")) return s;
            if (s.StartsWith("972")) return "+" + s;
            if (s.StartsWith("0")) return "+972" + s.Substring(1);
            return "+" + s;
        }

        private static string Mask(string phone) =>
            string.IsNullOrEmpty(phone) || phone.Length < 4 ? "***" : new string('*', phone.Length - 3) + phone[^3..];
    }
}
