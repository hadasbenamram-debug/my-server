using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly FoodRescueDbContext _context;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IConfiguration configuration,
            FoodRescueDbContext context,
            ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;
        }

        public async Task SendEmailAsync(int? userId, string toEmail, string subject, string body)
        {
            var smtpServer = _configuration["EmailSettings:Server"];
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var senderPassword = _configuration["EmailSettings:SenderPassword"];

            if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(senderEmail))
            {
                throw new InvalidOperationException(
                    "EmailSettings לא מוגדר ב-appsettings.json (Server/SenderEmail ריקים).");
            }

            var port = int.TryParse(_configuration["EmailSettings:Port"], out var parsedPort) ? parsedPort : 587;

            // יצירת רשומת התראה עם אתחול מלא לכל שדות החובה בטבלה
            var notification = new Notification
            {
                UserId = (userId.HasValue && userId.Value > 0) ? userId.Value : 1,
                Channel = NotificationChannel.Email,
                Subject = string.IsNullOrWhiteSpace(subject) ? "ללא נושא" : subject,
                Content = string.IsNullOrWhiteSpace(body) ? "תוכן ריק" : body,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ErrorMessage = string.Empty,
                ExternalMessageId = string.Empty // חובה! מונע שגיאת NULL עבור שדה זה
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            try
            {
                using var client = new SmtpClient(smtpServer, port)
                {
                    Credentials = new NetworkCredential(senderEmail, senderPassword),
                    EnableSsl = true
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);

                // עדכון הסטטוס להצלחה לאחר שליחה תקינה
                notification.Status = NotificationStatus.Sent;
                notification.SentAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;

                if (innerMessage.Length > 500)
                {
                    innerMessage = innerMessage.Substring(0, 500);
                }

                _logger.LogError(ex, "שגיאה בשמירת התראה או שליחת מייל. סיבה: {Message}", innerMessage);

                notification.Status = NotificationStatus.Failed;
                notification.ErrorMessage = innerMessage;
                notification.RetryCount++;
                await _context.SaveChangesAsync();

                throw;
            }
        }
    }
}