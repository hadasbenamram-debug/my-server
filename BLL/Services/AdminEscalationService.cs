using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    /// <summary>
    /// אסקלציה למנהלים כשלא נמצא מתנדב לתרומה: מייל + התראה במרכז ההתראות.
    /// למניעת הצפה (המנוע רץ כל שעה) לא נשלחת התראה חוזרת לאותו מנהל על אותה תרומה
    /// בתוך 24 שעות, אלא אם ההתראה הקודמת נכשלה.
    ///
    /// תוקן: השירות יצר בעבר Notification בלי ExternalMessageId (עמודת חובה) - ה-INSERT נכשל,
    /// החריגה נבלעה, ושום התראה לא נשלחה. עכשיו ההתראה עוברת דרך תור ההתראות הרגיל.
    /// </summary>
    public class AdminEscalationService
    {
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromHours(24);

        private readonly IUserRepository _userRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationService _notifications;
        private readonly ILogger<AdminEscalationService> _logger;
        private readonly IConfiguration? _configuration;

        public AdminEscalationService(
            IUserRepository userRepository,
            INotificationRepository notificationRepository,
            INotificationService notifications,
            ILogger<AdminEscalationService> logger,
            // תיקון 37: לקישור "לשיבוץ ידני" במייל (App:FrontendBaseUrl)
            IConfiguration? configuration = null)
        {
            _configuration = configuration;
            _userRepository = userRepository;
            _notificationRepository = notificationRepository;
            _notifications = notifications;
            _logger = logger;
        }

        public static string SubjectFor(int donationId) => $"אין מתנדב זמין לתרומה #{donationId}";

        // מחזיר כמה מנהלים קיבלו התראה (0 = כולם כבר קיבלו לאחרונה / אין מנהלים)
        public async Task<int> NotifyAdminsAsync(Donation donation, string reason)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));

            var admins = await _userRepository.GetByRoleAsync(UserRole.Admin);
            if (admins.Count == 0)
            {
                _logger.LogWarning("Escalation for donation {DonationId}: there are no admins to notify.", donation.Id);
                return 0;
            }

            string subject = SubjectFor(donation.Id);
            // תיקון 37: מייל מעוצב (EmailTemplate). תיקון 36: אפשר לשבץ ידנית למתנדב שהמנהל בוחר (גם בתיאום טלפוני)
            string content =
                EmailTemplate.Paragraph($"המנוע לא הצליח לשבץ מתנדב לתרומה <b>#{donation.Id}</b>, ונדרש טיפול ידני.") +
                EmailTemplate.Details(
                    ("סיבה", WebUtility.HtmlEncode(reason)),
                    ("שעת איסוף אחרונה", $"{donation.LatestPickupTime:hh\\:mm}"),
                    ("המזון טוב עד", $"{BLL.Algorithms.LocalClock.ToLocal(donation.ExpirationTime):dd/MM/yyyy HH:mm}")) +
                EmailTemplate.Paragraph("בעמוד <b>ניהול תרומות</b> אפשר לבחור מתנדב ולשלוח אליו את התרומה (שיבוץ ידני) - " +
                                        "גם מתנדב שתיאמתם איתו בטלפון, מחוץ למשמרת הזמינות שלו. אפשר גם לפנות ישירות לספק או לעמותה.") +
                (_configuration == null || FrontendLinks.TryBuild(_configuration, "app/admin/donations") is not string link
                    ? ""
                    : EmailTemplate.Button(link, "לשיבוץ ידני"));

            var since = DateTime.UtcNow - RepeatWindow;
            int notified = 0;

            foreach (var admin in admins)
            {
                if (await _notificationRepository.HasRecentNonFailedAsync(admin.Id, subject, since))
                    continue;

                try
                {
                    await _notifications.QueueEmailAsync(admin.Id, admin.Email, subject, content, summary: reason);
                    await _notifications.NotifyUserAsync(admin.Id, subject, reason, link: "/app/admin/donations", type: "warning");
                    notified++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Escalation notification to admin {AdminId} failed.", admin.Id);
                }
            }

            if (notified > 0)
                _logger.LogWarning("Escalation: no volunteer for donation {DonationId} ({Reason}); notified {Count} admins.", donation.Id, reason, notified);

            return notified;
        }
    }
}
