using DAL;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Notifications
{
    public interface INotificationService
    {
        // מייל: נשמר בתור (גוף מוצפן) ונשלח ברקע עם ניסיונות חוזרים - הבקשה לא מחכה לשרת המייל.
        // summary = תקציר לא רגיש (לא סיסמאות/קודים/קישורים עם טוקן).
        Task QueueEmailAsync(int? userId, string toEmail, string subject, string htmlBody, string? summary = null);

        // SMS: אותו מנגנון כמו מייל
        Task QueueSmsAsync(int? userId, string phoneNumber, string text, string? summary = null);

        // התראה במרכז ההתראות + דחיפה בזמן אמת (SignalR)
        Task NotifyUserAsync(int userId, string subject, string? content, string? link = null, string type = "info");

        // התראה לכל המנהלים
        Task NotifyAdminsAsync(string subject, string? content, string? link = null, string type = "info");

        // אירוע בזמן אמת בלבד, בלי לשמור (עדכון סטטוס / מיקום)
        Task PublishAsync(string group, string eventName, object payload);
    }

    public sealed class NotificationService : INotificationService
    {
        private readonly INotificationRepository _repository;
        private readonly IUserRepository _userRepository;
        private readonly IPayloadProtector _protector;
        private readonly IBackgroundJobClient _jobs;
        private readonly IRealtimeNotifier _realtime;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            INotificationRepository repository,
            IUserRepository userRepository,
            IPayloadProtector protector,
            IBackgroundJobClient jobs,
            IRealtimeNotifier realtime,
            ILogger<NotificationService> logger)
        {
            _repository = repository;
            _userRepository = userRepository;
            _protector = protector;
            _jobs = jobs;
            _realtime = realtime;
            _logger = logger;
        }

        public Task QueueEmailAsync(int? userId, string toEmail, string subject, string htmlBody, string? summary = null) =>
            QueueAsync(NotificationChannel.Email, userId, toEmail, subject, htmlBody, summary);

        public Task QueueSmsAsync(int? userId, string phoneNumber, string text, string? summary = null) =>
            QueueAsync(NotificationChannel.Sms, userId, phoneNumber, null, text, summary);

        private async Task QueueAsync(NotificationChannel channel, int? userId, string recipient, string? subject, string body, string? summary)
        {
            if (string.IsNullOrWhiteSpace(recipient))
                throw new ArgumentException("לא צוין נמען להודעה.");

            var notification = new Notification
            {
                // תוקן: פנייה מאורח נרשמה בעבר על משתמש 1 קבוע. עכשיו null = "לא משתמש רשום".
                UserId = userId is > 0 ? userId : null,
                Channel = channel,
                Recipient = Truncate(recipient.Trim(), 254),
                Subject = subject == null ? null : Truncate(subject, 200),
                Content = summary == null ? null : Truncate(summary, 500),
                ProtectedPayload = _protector.Protect(body ?? string.Empty),
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(notification);

            try
            {
                _jobs.Enqueue<NotificationDispatcher>(d => d.DispatchAsync(notification.Id));
            }
            catch (Exception ex)
            {
                // אם Hangfire לא זמין כרגע - הסריקה התקופתית (SweepAsync) תשלח את ההודעה
                _logger.LogWarning(ex, "Could not enqueue notification {NotificationId}; the periodic sweep will send it.", notification.Id);
            }
        }

        public async Task NotifyUserAsync(int userId, string subject, string? content, string? link = null, string type = "info")
        {
            var notification = new Notification
            {
                UserId = userId,
                Channel = NotificationChannel.InApp,
                Subject = Truncate(subject, 200),
                Content = content == null ? null : Truncate(content, 500),
                Link = link == null ? null : Truncate(link, 300),
                Status = NotificationStatus.Sent,
                CreatedAt = DateTime.UtcNow,
                SentAt = DateTime.UtcNow
            };

            await _repository.AddAsync(notification);

            await SafePushAsync(() => _realtime.SendToUserAsync(userId, RealtimeEvents.Notification, ToDto(notification, type)));
        }

        public async Task NotifyAdminsAsync(string subject, string? content, string? link = null, string type = "info")
        {
            var admins = await _userRepository.GetByRoleAsync(UserRole.Admin);
            foreach (var admin in admins)
                await NotifyUserAsync(admin.Id, subject, content, link, type);
        }

        public Task PublishAsync(string group, string eventName, object payload) =>
            SafePushAsync(() => _realtime.SendToGroupAsync(group, eventName, payload));

        public static NotificationDto ToDto(Notification n, string type = "info") => new NotificationDto
        {
            Id = n.Id,
            Type = type,
            Subject = n.Subject,
            Content = n.Content,
            Link = n.Link,
            CreatedAt = n.CreatedAt,
            IsRead = n.ReadAt != null
        };

        // כשל בדחיפה בזמן אמת לא מכשיל את הפעולה העסקית
        private async Task SafePushAsync(Func<Task> push)
        {
            try { await push(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Real-time push failed."); }
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max);
    }
}
