using BLL.Interfaces;
using BLL.Notifications;
using FoodRescue.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class SupportService : ISupportService
    {
        private readonly INotificationService _notifications;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SupportService> _logger;

        public SupportService(
            INotificationService notifications,
            IConfiguration configuration,
            ILogger<SupportService> logger)
        {
            _notifications = notifications;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendContactMessageAsync(ContactMessageDto dto, int? userId)
        {
            // לאן הפנייה נשלחת: SupportSettings:InboxEmail, ואם לא הוגדר - מייל המנהל, ואם גם לא - כתובת השולח
            var inbox = FirstNonEmpty(
                _configuration["SupportSettings:InboxEmail"],
                _configuration["AdminSeed:Email"],
                _configuration["EmailSettings:SenderEmail"]);

            if (inbox == null)
            {
                throw new InvalidOperationException(
                    "לא הוגדרה תיבת תמיכה (SupportSettings:InboxEmail / AdminSeed:Email).");
            }

            // קידוד HTML - המייל נשלח כ-HTML, ואסור להכניס אליו טקסט משתמש כמו שהוא
            var name = WebUtility.HtmlEncode(dto.Name.Trim());
            var email = WebUtility.HtmlEncode(dto.Email.Trim());
            var phone = WebUtility.HtmlEncode(dto.Phone?.Trim() ?? "");
            var subject = dto.Subject.Trim();
            var message = WebUtility.HtmlEncode(dto.Message.Trim()).Replace("\n", "<br/>");

            // תיקון 37: מייל מעוצב (EmailTemplate) - פרטי הפונה בטבלה, וההודעה בתיבה מודגשת
            var adminBody =
                EmailTemplate.Paragraph("התקבלה פנייה חדשה מטופס צור קשר באתר:") +
                EmailTemplate.Details(
                    ("שם", name),
                    ("אימייל", $"<a href=\"mailto:{email}\">{email}</a>"),
                    ("טלפון", phone),
                    ("נושא", WebUtility.HtmlEncode(subject))) +
                EmailTemplate.Quote(message) +
                EmailTemplate.Note("כדי לענות לפונה - משיבים למייל שלו (הכתובת שבטבלה).");

            // תוקן: פנייה מאורח (userId == null) נרשמת בלי משתמש, ולא על משתמש 1.
            // המיילים נכנסים לתור ונשלחים ברקע עם ניסיונות חוזרים.
            // 1. המייל לצוות - זה החלק שחייב להצליח
            await _notifications.QueueEmailAsync(userId, inbox, $"[צור קשר] {subject}", adminBody,
                summary: "פנייה מטופס צור קשר");

            // 2. אישור קבלה לפונה - אם נכשל, לא מכשילים את כל הפנייה
            try
            {
                var confirmBody =
                    EmailTemplate.Greeting(dto.Name.Trim()) +
                    EmailTemplate.Paragraph($"קיבלנו את פנייתך בנושא <b>&quot;{WebUtility.HtmlEncode(subject)}&quot;</b>, ונחזור אליך בהקדם.") +
                    EmailTemplate.Paragraph("בינתיים - תודה שאת/ה חלק מהקהילה שמצילה מזון ומביאה אותו למי שזקוק לו.") +
                    EmailTemplate.Signature();
                await _notifications.QueueEmailAsync(userId, dto.Email.Trim(), "קיבלנו את פנייתך", confirmBody,
                    summary: "אישור קבלת פנייה");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "הכנסת אישור קבלה לתור נכשלה.");
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }
    }
}
