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

    // שליחה בפועל של SMS. מחזיר מזהה הודעה אצל הספק (אם יש).
    public interface ISmsSender
    {
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

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mailMessage.To.Add(toEmail);
            mailMessage.Headers.Add("Message-ID", messageId);

            await client.SendMailAsync(mailMessage);
            return messageId;
        }
    }

    // SMS: Sms:Provider = "Twilio" (Sms:AccountSid, Sms:AuthToken, Sms:From) או "Log" (פיתוח - רק לוג).
    // נשלח ישירות ל-REST API של Twilio, בלי חבילה חיצונית.
    public sealed class SmsSender : ISmsSender
    {
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

        public async Task<string?> SendAsync(string phoneNumber, string text)
        {
            var provider = _configuration["Sms:Provider"];

            if (string.Equals(provider, "Twilio", StringComparison.OrdinalIgnoreCase))
            {
                var sid = _configuration["Sms:AccountSid"];
                var token = _configuration["Sms:AuthToken"];
                var from = _configuration["Sms:From"];
                if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
                    throw new InvalidOperationException("Sms:AccountSid / Sms:AuthToken / Sms:From are not configured.");

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

            if (string.Equals(provider, "Log", StringComparison.OrdinalIgnoreCase) || _environment.IsDevelopment())
            {
                _logger.LogWarning("SMS provider is not configured (Sms:Provider). SMS to {Phone} was not sent.", Mask(phoneNumber));
                return "dev-not-sent";
            }

            throw new InvalidOperationException("Sms:Provider is not configured.");
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
