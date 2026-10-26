using System;

namespace BLL.Algorithms
{
    /// <summary>
    /// מתי רישיון נהיגה של מתנדב נחשב "בתוקף": הוזן תאריך תוקף, והוא לא לפני היום - לפי התאריך בישראל
    /// (בעבר הושווה לתאריך ה-UTC, כך שבין חצות ל-02:00/03:00 רישיון שפג אתמול עוד נחשב בתוקף).
    /// אותו כלל בדיוק בשלושה מקומות: מנוע השידוכים (בחירת המתנדב), אישור הבקשה ע"י המתנדב, ואישור המתנדב ע"י המנהל.
    /// </summary>
    public static class DriverLicensePolicy
    {
        public const string NotValidMessage = "רישיון הנהיגה אינו בתוקף (פג או שלא הוזן תאריך תוקף).";

        /// <summary>התאריך של היום בשעון ישראל.</summary>
        public static DateTime TodayLocal(DateTime utcNow) => LocalClock.ToLocal(utcNow).Date;

        /// <summary>רישיון בלי תאריך תוקף לא נחשב בתוקף - אי אפשר לדעת שהוא תקף.</summary>
        public static bool IsValid(DateTime? expiry, DateTime utcNow) =>
            expiry.HasValue && expiry.Value.Date >= TodayLocal(utcNow);
    }
}
