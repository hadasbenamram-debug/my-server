using DAL.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DAL
{
    // עודכן: מיילים כעת נשלחים ישירות מקוד ה-C# (SMTP/SendGrid וכו') ולא דרך n8n.
    // n8n ניהל בעבר retry/backoff, בניית נושא, ולוג ריצות - כל זה עובר עכשיו
    // לאחריות המערכת שלנו, ולכן נוספו השדות הבאים.
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        [Required] public NotificationChannel Channel { get; set; }

        // נוסף: נפרד מ-Content כי ה-BLL עצמו מרכיב את המייל (נושא+גוף).
        // רלוונטי רק לערוץ Email - null/ריק כשה-Channel הוא Sms.
        public string Subject { get; set; }

        [Required] public string Content { get; set; }
        public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

        // נוסף: מונה ניסיונות שליחה (retry/backoff שהיה מובנה בעבר ב-n8n).
        public int RetryCount { get; set; } = 0;

        // נוסף: פירוט שגיאה אם השליחה נכשלה (חריגת SMTP וכו').
        public string ErrorMessage { get; set; }

        // נוסף: מזהה הודעה חיצוני מספק השליחה (למשל message-id של SendGrid),
        // למעקב/תמיכה אם משתמש טוען שלא קיבל את ההודעה.
        [MaxLength(255)] public string ExternalMessageId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }
}