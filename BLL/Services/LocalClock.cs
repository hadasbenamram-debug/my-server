using System;

namespace BLL.Algorithms
{
    /// <summary>
    /// קובץ חדש. המערכת שומרת DateTime בשעון UTC (CreatedAt, ExpirationTime, AssignedAt...)
    /// אבל כל "שעון קיר" שאדם מגדיר (שעות קבלה של עמותה, מערכת שעות של מתנדב, LatestPickupTime)
    /// הוא שעון מקומי. בקוד המקורי הושוו ביניהם ישירות (UTC מול שעה מקומית) – פער של 2-3 שעות
    /// שגרם, למשל, לקביעה שעמותה סגורה כשהיא פתוחה (ולהפך), וליום שישי "שמתחיל" ב-02:00/03:00.
    ///
    /// כל ההמרות עוברות דרך המחלקה הזו. ברירת המחדל: Asia/Jerusalem.
    /// אם בפועל כל הנתונים שלכם שמורים כ"שעון קיר" ללא המרה — הגדירו MatchingPolicy:TimeZoneId = "UTC"
    /// והתנהגות המערכת תחזור לזו של הקוד המקורי.
    /// </summary>
    public static class LocalClock
    {
        private static volatile TimeZoneInfo _zone = ResolveDefault();

        public static TimeZoneInfo Zone => _zone;

        /// <summary>נקראת פעם אחת בעליית האפליקציה (Program.cs).</summary>
        public static void Configure(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) return;

            if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            {
                _zone = TimeZoneInfo.Utc;
                return;
            }

            try
            {
                _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException) { /* נשארים עם ברירת המחדל */ }
            catch (InvalidTimeZoneException) { /* נשארים עם ברירת המחדל */ }
        }

        public static DateTime ToLocal(DateTime utc)
        {
            var normalized = utc.Kind == DateTimeKind.Local
                ? utc.ToUniversalTime()
                : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(normalized, _zone);
        }

        public static DateTime ToUtc(DateTime local)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            // שעה "שלא קיימת" (מעבר לשעון קיץ) – מקדמים בשעה במקום לזרוק חריגה
            if (_zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, _zone);
        }

        private static TimeZoneInfo ResolveDefault()
        {
            foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }
    }
}
