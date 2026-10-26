using BLL.Configuration;
using System;

namespace BLL.Algorithms
{
    /// <summary>
    /// חדש (תיקון 9): זמני השבת, כדי שמנוע השידוכים לא ישלח הצעות ולא ישבץ מסלולים בשבת.
    ///
    /// השבת = מכניסת השבת ביום שישי (CandleLightingMinutesBeforeSunset דקות לפני השקיעה)
    /// ועד צאת השבת (ShabbatEndMinutesAfterSunset דקות אחרי השקיעה במוצאי שבת).
    /// השקיעה מחושבת לפי האלגוריתם של NOAA (דיוק של כדקה), במיקום שמוגדר ב-MatchingPolicy
    /// (ברירת מחדל: ירושלים). הזמנים מוחזרים באותו ייצוג כמו שאר התאריכים במערכת (UTC).
    /// </summary>
    public static class ShabbatClock
    {
        /// <summary>
        /// משבצת זמינות של מתנדב / שעות קבלה של עמותה במוצאי שבת לא מתחילות לפני השעה הזו
        /// (צאת השבת המוקדם ביותר בשנה בישראל הוא בסביבות 17:15).
        /// </summary>
        public static readonly TimeSpan EarliestSaturdayNightTime = new(17, 0, 0);

        public const string EngineMessage = "שבת - לא נשלחות הצעות ולא מתבצעים שיבוצים עד צאת השבת.";

        public readonly record struct ShabbatWindow(DateTime StartUtc, DateTime EndUtc);

        /// <summary>האם הרגע הזה בתוך השבת.</summary>
        public static bool IsShabbat(DateTime utc, MatchingPolicyOptions policy) => Overlaps(utc, utc, policy);

        /// <summary>האם הטווח [startUtc, endUtc] נוגע בשבת (גם טווח שמתחיל לפני השבת ונגמר בתוכה).</summary>
        public static bool Overlaps(DateTime startUtc, DateTime endUtc, MatchingPolicyOptions policy)
        {
            if (policy == null || !policy.ObserveShabbat) return false;
            if (endUtc < startUtc) (startUtc, endUtc) = (endUtc, startUtc);

            // השבת הראשונה שמסתיימת אחרי תחילת הטווח: אם היא מתחילה לפני סוף הטווח - יש חפיפה
            return CurrentOrNext(startUtc, policy).StartUtc <= endUtc;
        }

        /// <summary>השבת הנוכחית (אם הרגע בתוך שבת) או השבת הקרובה.</summary>
        public static ShabbatWindow CurrentOrNext(DateTime utc, MatchingPolicyOptions policy)
        {
            var localDate = LocalClock.ToLocal(utc).Date;
            int daysToSaturday = ((int)DayOfWeek.Saturday - (int)localDate.DayOfWeek + 7) % 7;
            var window = ForSaturday(localDate.AddDays(daysToSaturday), policy);

            // מוצאי שבת אחרי צאת השבת - השבת של השבוע הבא
            return window.EndUtc > utc ? window : ForSaturday(localDate.AddDays(daysToSaturday + 7), policy);
        }

        /// <summary>זמני השבת שחלה בתאריך המקומי saturday (יום שבת).</summary>
        public static ShabbatWindow ForSaturday(DateTime saturday, MatchingPolicyOptions policy)
        {
            var friday = saturday.Date.AddDays(-1);
            var start = SunsetUtc(friday, policy.ShabbatLatitude, policy.ShabbatLongitude)
                .AddMinutes(-Math.Max(0, policy.CandleLightingMinutesBeforeSunset));
            var end = SunsetUtc(saturday.Date, policy.ShabbatLatitude, policy.ShabbatLongitude)
                .AddMinutes(Math.Max(0, policy.ShabbatEndMinutesAfterSunset));

            return new ShabbatWindow(ToSystemTime(start), ToSystemTime(end));
        }

        /// <summary>
        /// רגע השקיעה (UTC) בתאריך date, במיקום latitude / longitude (מזרח = חיובי).
        /// לפי NOAA Solar Calculator: מיקום השמש מחושב סביב שעת השקיעה עצמה (שלושה סבבי תיקון).
        /// </summary>
        public static DateTime SunsetUtc(DateTime date, double latitude, double longitude)
        {
            var midnightUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

            // ניחוש ראשון: שש שעות אחרי הצהריים הסולאריים
            double minutes = 720 - 4 * longitude + 360;
            for (int i = 0; i < 3; i++)
                minutes = SunsetMinutesAfterMidnightUtc(midnightUtc.AddMinutes(minutes), latitude, longitude);

            return midnightUtc.AddMinutes(minutes);
        }

        // דקות מחצות (UTC) ועד השקיעה, לפי מיקום השמש ברגע atUtc
        private static double SunsetMinutesAfterMidnightUtc(DateTime atUtc, double latitude, double longitude)
        {
            double julianDay = (atUtc - DateTime.UnixEpoch).TotalDays + 2440587.5;
            double t = (julianDay - 2451545.0) / 36525.0;

            double meanLongitude = Normalize(280.46646 + t * (36000.76983 + t * 0.0003032));
            double meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
            double center = Sin(meanAnomaly) * (1.914602 - t * (0.004817 + 0.000014 * t))
                          + Sin(2 * meanAnomaly) * (0.019993 - 0.000101 * t)
                          + Sin(3 * meanAnomaly) * 0.000289;
            double omega = 125.04 - 1934.136 * t;
            double apparentLongitude = meanLongitude + center - 0.00569 - 0.00478 * Sin(omega);
            double meanObliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
            double obliquity = meanObliquity + 0.00256 * Cos(omega);
            double declination = Deg(Math.Asin(Sin(obliquity) * Sin(apparentLongitude)));

            double y = Math.Pow(Math.Tan(Rad(obliquity / 2)), 2);
            double equationOfTime = 4 * Deg(
                y * Sin(2 * meanLongitude)
                - 2 * eccentricity * Sin(meanAnomaly)
                + 4 * eccentricity * y * Sin(meanAnomaly) * Cos(2 * meanLongitude)
                - 0.5 * y * y * Sin(4 * meanLongitude)
                - 1.25 * eccentricity * eccentricity * Sin(2 * meanAnomaly));

            // 90.833 מעלות: שבירת האור באטמוספרה + רדיוס השמש
            double cosHourAngle = Cos(90.833) / (Cos(latitude) * Cos(declination)) - Tan(latitude) * Tan(declination);
            double hourAngle = Deg(Math.Acos(Math.Clamp(cosHourAngle, -1.0, 1.0)));

            double solarNoon = 720 - 4 * longitude - equationOfTime;
            return solarNoon + 4 * hourAngle;
        }

        // במצב התאימות TimeZoneId = "UTC" התאריכים במערכת הם "שעון קיר" (ראו LocalClock),
        // ולכן את רגע השקיעה ממירים לשעון ישראל לפני ההשוואה אליהם.
        private static DateTime ToSystemTime(DateTime utc)
        {
            var zone = LocalClock.Zone;
            if (zone.BaseUtcOffset != TimeSpan.Zero || zone.SupportsDaylightSavingTime)
                return utc;

            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, IsraelZone.Value), DateTimeKind.Utc);
        }

        private static readonly Lazy<TimeZoneInfo> IsraelZone = new(() =>
        {
            foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        });

        private static double Rad(double degrees) => degrees * Math.PI / 180.0;
        private static double Deg(double radians) => radians * 180.0 / Math.PI;
        private static double Sin(double degrees) => Math.Sin(Rad(degrees));
        private static double Cos(double degrees) => Math.Cos(Rad(degrees));
        private static double Tan(double degrees) => Math.Tan(Rad(degrees));
        private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;
    }
}
