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

            var body =
                $"שלום {WebUtility.HtmlEncode(existingUser.Name)},<br/>" +
                $"קיבלנו בקשת הרשמה ל\"לתת באהבה\"{what} עם כתובת המייל הזו - אבל כבר יש לך חשבון אצלנו, ולכן לא נוצר חשבון חדש.<br/><br/>" +
                (loginUrl == null ? "" : $"אפשר להתחבר עם המייל והסיסמה שלך: <a href='{loginUrl}'>כניסה למערכת</a><br/>") +
                (forgotUrl == null ? "" : $"שכחת את הסיסמה? <a href='{forgotUrl}'>איפוס סיסמה</a><br/>") +
                "<br/>אם לא ביקשת להירשם - אפשר פשוט להתעלם מההודעה הזו.<br/><br/>" +
                "תודה,<br/>צוות לתת באהבה";

            await _notifications.QueueEmailAsync(existingUser.Id, existingUser.Email, Subject, body,
                summary: "ניסיון הרשמה עם מייל קיים");
        }
    }
}
