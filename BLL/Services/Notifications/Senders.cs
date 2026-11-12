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
using System.Xml.Linq;

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

        // תיקון 64: שיחה קולית - הספק מתקשר למספר ומקריא את הטקסט בקול (Text-to-Speech) בעברית.
        // אותו ספק ואותם פרטים כמו ה-SMS. מתאים גם לטלפון שלא מקבל מסרונים.
        // מימושי ברירת מחדל - למחלקות (למשל בבדיקות) שלא מבצעות שיחות.
        SmsMode VoiceMode => SmsMode.NotConfigured;

        Task<string?> CallAsync(string phoneNumber, string spokenText) =>
            throw new NotSupportedException("Voice calls are not supported by this sender.");
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
    // תיקון 64 - שיחה קולית (CallAsync) דרך אותו ספק:
    //   "019"    - אותו API עם אובייקט tts (type 4 = שיחה קולית בלבד). השיחה יוצאת ממספר מערכת של 019.
    //   "Twilio" - Calls API עם TwiML (<Say> בעברית, קול של Google). צריך מספר טלפון של Twilio:
    //              Sms:VoiceFrom (או Sms:From אם הוא מספר ולא שם שולח באותיות).
    //   Sms:VoiceGender - "Male" (ברירת מחדל) או "Female".
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

        public SmsMode VoiceMode => ResolveVoiceMode(_configuration, _environment);

        // תיקון 64: שיחה קולית אפשרית כשה-SMS מוגדר, ובטוויליו - רק עם מספר טלפון ממנו מתקשרים (לא שם שולח באותיות)
        public static SmsMode ResolveVoiceMode(IConfiguration configuration, IHostEnvironment environment)
        {
            var mode = ResolveMode(configuration, environment);
            if (mode == SmsMode.Twilio && !IsPhoneNumber(TwilioVoiceFrom(configuration)))
                return SmsMode.NotConfigured;
            return mode;
        }

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

        // תיקון 64: שיחה קולית שמקריאה את הטקסט
        public async Task<string?> CallAsync(string phoneNumber, string spokenText)
        {
            switch (VoiceMode)
            {
                case SmsMode.Twilio:
                    return await CallTwilioAsync(phoneNumber, spokenText);

                case SmsMode.Israel019:
                    return await Call019Async(phoneNumber, spokenText);

                case SmsMode.DevelopmentLog:
                    _logger.LogWarning("Development: voice provider is not configured (Sms:Provider), the call was NOT made. To {Phone}: {Text}",
                        Mask(phoneNumber), spokenText);
                    return "dev-log-only";

                default:
                    throw new InvalidOperationException(
                        "Voice calls are not configured. Set Sms:Provider to \"019\", or to \"Twilio\" with a Twilio phone number in Sms:VoiceFrom.");
            }
        }

        private bool IsFemaleVoice =>
            string.Equals(_configuration["Sms:VoiceGender"]?.Trim(), "Female", StringComparison.OrdinalIgnoreCase);

        private static string? TwilioVoiceFrom(IConfiguration configuration)
        {
            var voiceFrom = configuration["Sms:VoiceFrom"];
            return string.IsNullOrWhiteSpace(voiceFrom) ? configuration["Sms:From"] : voiceFrom;
        }

        // מספר טלפון (+972..., 05...) ולא שם שולח באותיות (Alphanumeric Sender ID) - ממנו אי אפשר להתקשר
        private static bool IsPhoneNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var digits = 0;
            foreach (var c in value.Trim())
            {
                if (char.IsAsciiDigit(c)) digits++;
                else if (c != '+' && c != '-' && c != ' ' && c != '(' && c != ')') return false;
            }
            return digits >= 7;
        }

        // Twilio: POST .../Calls.json עם Twiml - ההוראות לשיחה נשלחות ישר בבקשה (בלי כתובת שטוויליו צריך לגשת אליה).
        // הטקסט מוקרא פעמיים, עם הפסקה קצרה בהתחלה (עד שמרימים) ובין הפעמים.
        private async Task<string?> CallTwilioAsync(string phoneNumber, string spokenText)
        {
            var sid = _configuration["Sms:AccountSid"]!;
            var token = _configuration["Sms:AuthToken"]!;

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Calls.json");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = ToE164(phoneNumber),
                ["From"] = ToE164(TwilioVoiceFrom(_configuration)!),
                ["Twiml"] = BuildTwiml(spokenText, IsFemaleVoice)
            });

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Twilio voice call returned HTTP {(int)response.StatusCode}.");

            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
        }

        // TwiML בעברית: <Say language="he-IL" voice="Google.he-IL-Wavenet-B"> (הטקסט עובר escape של XML)
        public static string BuildTwiml(string spokenText, bool female = false)
        {
            var voice = female ? "Google.he-IL-Wavenet-A" : "Google.he-IL-Wavenet-B";
            XElement Say() => new("Say", new XAttribute("language", "he-IL"), new XAttribute("voice", voice), spokenText);
            XElement Pause() => new("Pause", new XAttribute("length", "1"));
            return new XElement("Response", Pause(), Say(), Pause(), Say()).ToString(SaveOptions.DisableFormatting);
        }

        // 019: אותו API כמו SMS, ועוד אובייקט tts (docs.019sms.co.il/sms/send-sms.html - "Send SMS with TTS"):
        // type 4 = שיחה קולית בלבד לכל המספרים, repeat 2 = ההודעה מוקראת פעמיים, voice ymMale / ymFemale.
        private async Task<string?> Call019Async(string phoneNumber, string spokenText)
        {
            var payload = new
            {
                sms = new
                {
                    user = new { username = _configuration["Sms:Username"] },
                    source = _configuration["Sms:Source"],
                    destinations = new { phone = new[] { new Dictionary<string, string> { ["_"] = ToIsraeliLocal(phoneNumber) } } },
                    message = spokenText,
                    tts = new { type = 4, rate = 0, repeat = 2, voice = IsFemaleVoice ? "ymFemale" : "ymMale" }
                }
            };

            return await Post019Async(payload, "019 voice call");
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

            return await Post019Async(payload, "019 SMS");
        }

        // תיקון 64: השליחה עצמה ובדיקת התשובה - משותפת ל-SMS ולשיחה הקולית
        private async Task<string?> Post019Async(object payload, string what)
        {
            var url = _configuration["Sms:Url"];
            using var request = new HttpRequestMessage(HttpMethod.Post, string.IsNullOrWhiteSpace(url) ? Default019Url : url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["Sms:ApiToken"]);
            request.Content = new StringContent(JsonSerializer.Serialize(payload, payload.GetType()), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{what} returned HTTP {(int)response.StatusCode}.");

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
                throw new InvalidOperationException($"{what} returned status {status}: {message}");
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
