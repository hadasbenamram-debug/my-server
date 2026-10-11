using BLL.Algorithms;
using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    /// <summary>
    /// אסקלציה למנהלים כשלא נמצא מתנדב לתרומה: לכל מנהל נוצרת רשומת Notification (Email)
    /// ונשלח מייל. למניעת הצפה (המנוע רץ כל שעה) לא נשלחת התראה חוזרת לאותו מנהל
    /// על אותה תרומה בתוך 24 שעות, אלא אם ההתראה הקודמת נכשלה.
    /// </summary>
    public class AdminEscalationService
    {
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromHours(24);

        private readonly FoodRescueDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AdminEscalationService> _logger;

        public AdminEscalationService(
            FoodRescueDbContext context,
            IEmailService emailService,
            ILogger<AdminEscalationService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task NotifyAdminsAsync(Donation donation, AssignmentOutcome outcome)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));

            _logger.LogWarning("אסקלציה: לא נמצא מתנדב לתרומה {DonationId} ({Outcome}).", donation.Id, outcome);

            var admins = await _context.Users.Where(u => u.Role == UserRole.Admin).ToListAsync();
            if (admins.Count == 0)
            {
                _logger.LogWarning("אסקלציה לתרומה {DonationId}: לא קיימים מנהלים במערכת להתראה.", donation.Id);
                return;
            }

            string reason = outcome == AssignmentOutcome.EscalatedOutOfRange
                ? "כל המתנדבים הזמינים נמצאים מחוץ לטווח החיפוש."
                : "לא נמצא אף מתנדב זמין ומתאים.";

            string subject = $"אין מתנדב זמין לתרומה #{donation.Id}";
            string content =
                $"לא ניתן לשבץ מתנדב לתרומה #{donation.Id}.<br/>" +
                $"סיבה: {WebUtility.HtmlEncode(reason)}<br/>" +
                $"שעת איסוף אחרונה: {donation.LatestPickupTime:hh\\:mm}<br/>" +
                $"תאריך תפוגה: {donation.ExpirationTime:yyyy-MM-dd HH:mm}<br/>" +
                "נדרש טיפול ידני (למשל פנייה ישירה למתנדבים או לעמותה).";

            var since = DateTime.UtcNow - RepeatWindow;

            foreach (var admin in admins)
            {
                bool alreadyNotified = await _context.Notifications.AnyAsync(n =>
                    n.UserId == admin.Id &&
                    n.Subject == subject &&
                    n.CreatedAt >= since &&
                    n.Status != NotificationStatus.Failed);
                if (alreadyNotified) continue;
                var notification = new Notification
                {
                    UserId = admin.Id,
                    Channel = NotificationChannel.Email,
                    Subject = subject,
                    Content = content,
                    Status = NotificationStatus.Pending,
                    ErrorMessage = string.Empty // חובה! מונע את שגיאת ה-NULL במסד הנתונים
                };
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.SendEmailAsync(admin.Id, admin.Email, subject, content);
                    notification.Status = NotificationStatus.Sent;
                    notification.SentAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    notification.Status = NotificationStatus.Failed;
                    notification.RetryCount = 1;
                    notification.ErrorMessage = ex.Message;
                    _logger.LogError(ex, "שליחת התראת אסקלציה למנהל {AdminId} נכשלה.", admin.Id);
                }

                await _context.SaveChangesAsync();
            }
        }
    }
}
