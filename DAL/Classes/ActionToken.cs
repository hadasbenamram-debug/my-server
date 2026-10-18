using System;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public enum ActionTokenPurpose
    {
        PasswordReset = 0,
        EmailVerification = 1,
        PhoneVerification = 2,
        MatchResponse = 3,   // קישור "אשר/דחה" במייל למתנדב - מוגבל למסלול אחד ולפעולה אחת
        ImageUpload = 4      // תמונת תרומה שהועלתה ע"י משתמש ועדיין לא שויכה לתרומה
    }

    // טוקן חד-פעמי ומוגבל-מטרה (קוד אימות, קישור איפוס סיסמה, קישור תגובה לשיבוץ...).
    // נשמר במסד ולא ב-MemoryCache, ולכן שורד הפעלה מחדש ועובד גם עם כמה שרתים.
    // הטוקן עצמו לא נשמר - רק Hash (SHA-256) שלו.
    public class ActionToken
    {
        [Key]
        public int Id { get; set; }

        public int? UserId { get; set; }
        public User? User { get; set; }

        public ActionTokenPurpose Purpose { get; set; }

        [Required, MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        // מזהה הישות שהטוקן מוגבל אליה (למשל מזהה המסלול / השידוך)
        public int? TargetId { get; set; }

        // ערך נוסף שהטוקן מוגבל אליו (למשל שם קובץ התמונה)
        [MaxLength(260)]
        public string? TargetValue { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; set; }

        // ניסיונות שגויים (לקודי אימות בני 6 ספרות)
        public int FailedAttempts { get; set; }
    }
}
