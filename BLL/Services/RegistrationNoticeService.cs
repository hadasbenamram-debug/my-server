using BLL.Interfaces;
using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    /// <summary>
    /// הרשמה עם מייל שכבר רשום במערכת. בעבר השרת זרק ConflictException עם ההודעה
    /// "כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו" - כך שכל אחד יכול היה לבדוק אילו מיילים רשומים,
    /// ובהרצה מ-Visual Studio החריגה גם עצרה את השרת בדיבאגר.
    /// עכשיו אין חריגה: הלקוח מקבל את אותה תשובה כמו בהרשמה חדשה, והמידע נשלח רק לתיבת המייל של בעל החשבון.
    /// </summary>
    public sealed class RegistrationNoticeService : IRegistrationNoticeService
    {
        public const string Subject = "ניסיון הרשמה עם כתובת המייל שלך - לתת באהבה";

        // לא יותר ממייל אחד כזה ברבע שעה לאותו חשבון (הגנה מהצפת תיבת הדואר)
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

        private readonly INotificationService _notifications;
        private readonly INotificationRepository _notificationRepository;
        private readonly IConfiguration _configuration;

        public RegistrationNoticeService(
            INotificationService notifications,
            INotificationRepository notificationRepository,
            IConfiguration configuration)
        {
            _notifications = notifications;
            _notificationRepository = notificationRepository;
            _configuration = configuration;
        }

        public async Task NotifyExistingAccountAsync(User existingUser, string? roleTitle)
        {
            if (existingUser == null || string.IsNullOrWhiteSpace(existingUser.Email))
                return;

            if (await _notificationRepository.HasRecentNonFailedAsync(existingUser.Id, Subject, DateTime.UtcNow - Cooldown))
                return;

            var loginUrl = FrontendLinks.TryBuild(_configuration, "app/login");
            var forgotUrl = FrontendLinks.TryBuild(_configuration, "app/forgot-password");
            string what = string.IsNullOrWhiteSpace(roleTitle) ? "" : $" ({roleTitle})";

            // תיקון 37: מייל מעוצב (EmailTemplate)
            var body =
                EmailTemplate.Greeting(existingUser.Name) +
                EmailTemplate.Paragraph($"קיבלנו בקשת הרשמה ל\"לתת באהבה\"{WebUtility.HtmlEncode(what)} עם כתובת המייל הזו - אבל כבר יש לך חשבון אצלנו, ולכן לא נוצר חשבון חדש.") +
                (loginUrl == null ? "" : EmailTemplate.Button(loginUrl, "כניסה למערכת")) +
                (forgotUrl == null ? "" : EmailTemplate.Paragraph($"שכחת את הסיסמה? <a href='{forgotUrl}'>איפוס סיסמה</a>")) +
                // תיקון 10: אותו חשבון יכול להחזיק כמה תפקידים - מסבירים איך מוסיפים תפקיד במקום להירשם מחדש
                EmailTemplate.Paragraph("רוצה להצטרף בתפקיד נוסף (למשל גם כמתנדב/ת וגם כספק) עם אותו חשבון? אחרי הכניסה למערכת, " +
                    "בעמוד <b>פרופיל אישי</b> יש כרטיס <b>התפקידים שלי</b> - שם בוחרים את התפקיד הנוסף וממלאים רק את הפרטים שלו " +
                    "(פרטים שכבר שמורים בחשבון, כמו תעודת הזהות, לא צריך להזין שוב).") +
                EmailTemplate.Note("אם לא ביקשת להירשם - אפשר פשוט להתעלם מההודעה הזו.") +
                EmailTemplate.Signature();

            await _notifications.QueueEmailAsync(existingUser.Id, existingUser.Email, Subject, body,
                summary: "ניסיון הרשמה עם מייל קיים");
        }

        public async Task NotifyRoleRequestReceivedAsync(User user, string roleTitle)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
                return;

            // תיקון 37: מייל מעוצב (EmailTemplate)
            var body =
                EmailTemplate.Greeting(user.Name) +
                EmailTemplate.Paragraph($"הבקשה שלך להצטרף גם {WebUtility.HtmlEncode(roleTitle)} התקבלה וממתינה לאישור מנהל.") +
                EmailTemplate.Paragraph("עד האישור אפשר להמשיך להשתמש בחשבון כרגיל. אחרי האישור יישלח אליך מייל, ותוכל/י לעבור בין התפקידים מהתפריט.") +
                EmailTemplate.Signature();

            await _notifications.QueueEmailAsync(user.Id, user.Email, "קיבלנו את הבקשה שלך להצטרף בתפקיד נוסף - לתת באהבה", body,
                summary: "בקשה לתפקיד נוסף התקבלה");
        }
    }
}
