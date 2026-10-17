using DAL.Classes;
using System;
using System.ComponentModel.DataAnnotations;

namespace DAL
{
    // רשומת התראה: גם "תיבת דואר יוצא" (Outbox) למיילים/SMS שנשלחים ברקע,
    // וגם התראות בתוך האפליקציה (InApp) שמוצגות במרכז ההתראות.
    //
    // אבטחה: גוף המייל/SMS לא נשמר כטקסט גלוי. בזמן ההמתנה לשליחה הוא נשמר מוצפן
    // (ProtectedPayload, AES-GCM) ונמחק מיד אחרי שליחה מוצלחת או כישלון סופי.
    // Content מכיל רק תקציר לא רגיש (למשל "בקשת איסוף חדשה") להצגה במרכז ההתראות.
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        // null = נמען שאינו משתמש רשום (למשל אורח שמילא טופס "צור קשר" או תיבת התמיכה)
        public int? UserId { get; set; }
        public User? User { get; set; }

        [Required] public NotificationChannel Channel { get; set; }

        // כתובת מייל / מספר טלפון. ריק בהתראת InApp.
        [MaxLength(254)] public string? Recipient { get; set; }

        [MaxLength(200)] public string? Subject { get; set; }

        // תקציר לא רגיש בלבד (לעולם לא סיסמאות, קודים או קישורים עם טוקן)
        [MaxLength(500)] public string? Content { get; set; }

        // גוף ההודעה המלא, מוצפן. null אחרי שליחה / כישלון סופי, ותמיד null ב-InApp.
        public string? ProtectedPayload { get; set; }

        public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

        // מספר ניסיונות שליחה שנכשלו
        public int RetryCount { get; set; } = 0;

        // מתי לנסות לשלוח שוב (Backoff). null = מיד.
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? LastAttemptAt { get; set; }

        [MaxLength(500)] public string? ErrorMessage { get; set; }

        // מזהה ההודעה אצל ספק השליחה (Message-Id / SID), אם התקבל
        [MaxLength(255)] public string? ExternalMessageId { get; set; }

        // קישור יחסי בתוך האפליקציה (למשל /app/volunteer/incoming-request/12) - ללא טוקנים
        [MaxLength(300)] public string? Link { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
