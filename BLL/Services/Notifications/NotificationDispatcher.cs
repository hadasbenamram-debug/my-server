using DAL;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Notifications
{
    // Hangfire job: שולח מייל / SMS מהתור. ניסיונות חוזרים עם השהיה הולכת וגדלה (RetryCount היה קיים
    // ב-Notification אבל אף פעם לא נוסה שליחה חוזרת). אחרי שליחה מוצלחת או כישלון סופי - הגוף המוצפן נמחק.
    public class NotificationDispatcher
    {
        public const int MaxAttempts = 5;
        private static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)
        };

        private readonly INotificationRepository _repository;
        private readonly IPayloadProtector _protector;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly ILogger<NotificationDispatcher> _logger;

        public NotificationDispatcher(
            INotificationRepository repository,
            IPayloadProtector protector,
            IEmailSender emailSender,
            ISmsSender smsSender,
            ILogger<NotificationDispatcher> logger)
        {
            _repository = repository;
            _protector = protector;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 0)]
        public async Task DispatchAsync(int notificationId)
        {
            var now = DateTime.UtcNow;

            // רק מי שהצליח "לתפוס" את ההודעה שולח אותה - גם אם הג'וב רץ פעמיים או על שני שרתים
            if (!await _repository.TryClaimForSendingAsync(notificationId, now))
                return;

            var notification = await _repository.GetFreshAsync(notificationId);
            if (notification == null)
                return;

            try
            {
                if (string.IsNullOrEmpty(notification.ProtectedPayload) || string.IsNullOrWhiteSpace(notification.Recipient))
                    throw new InvalidOperationException("Notification has no payload or recipient.");

                var body = _protector.Unprotect(notification.ProtectedPayload);

                notification.ExternalMessageId = notification.Channel switch
                {
                    NotificationChannel.Email => await _emailSender.SendAsync(notification.Recipient, notification.Subject ?? string.Empty, body),
                    NotificationChannel.Sms => await _smsSender.SendAsync(notification.Recipient, body),
                    _ => null
                };

                notification.Status = NotificationStatus.Sent;
                notification.SentAt = DateTime.UtcNow;
                notification.ErrorMessage = null;
                notification.ProtectedPayload = null;
            }
            catch (Exception ex)
            {
                notification.RetryCount++;
                notification.ErrorMessage = Truncate(ex.InnerException?.Message ?? ex.Message, 500);

                if (notification.RetryCount >= MaxAttempts || ex is System.Security.Cryptography.CryptographicException)
                {
                    notification.Status = NotificationStatus.Failed;
                    notification.ProtectedPayload = null;
                    _logger.LogError(ex, "Notification {NotificationId} failed permanently after {Attempts} attempts.", notification.Id, notification.RetryCount);
                }
                else
                {
                    notification.Status = NotificationStatus.Pending;
                    notification.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(notification.RetryCount - 1, Backoff.Length - 1)];
                    _logger.LogWarning(ex, "Notification {NotificationId} failed (attempt {Attempt}); will retry at {NextAttempt}.",
                        notification.Id, notification.RetryCount, notification.NextAttemptAt);
                }
            }

            await _repository.UpdateAsync(notification);
        }

        // Hangfire recurring job (כל דקה): שולח הודעות שהגיע זמנן (ניסיונות חוזרים / הודעות שלא נכנסו לתור)
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        [AutomaticRetry(Attempts = 0)]
        public async Task SweepAsync()
        {
            var now = DateTime.UtcNow;
            await _repository.ResetStuckSendingAsync(now.AddMinutes(-10));

            var due = await _repository.GetDueIdsAsync(now, take: 50);
            foreach (var id in due)
                await DispatchAsync(id);
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max);
    }
}
