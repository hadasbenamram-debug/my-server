using BLL.Algorithms;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BLL.Agent
{
    // תצוגה של נתונים בתוך התשובות של הכלים.
    // במסד הזמנים שמורים ב-UTC. אם נשלח אותם למודל כמו שהם, הוא יגיד למשתמש שעה לא נכונה (פער של 2-3 שעות),
    // ולכן כל כלי ממיר לשעון ישראל (LocalClock) ולטקסט קריא לפני שהוא מחזיר תשובה.
    // גם הסטטוסים מתורגמים לעברית - אותם ניסוחים שמופיעים במסכים (utils/format.ts בקליינט).
    internal static class AgentFormat
    {
        private static readonly string[] DayNames = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };

        // "יום חמישי 06/11 14:30"
        public static string DateAndTime(DateTime utc)
        {
            var local = LocalClock.ToLocal(utc);
            return $"יום {DayNames[(int)local.DayOfWeek]} {local.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}";
        }

        public static string? DateAndTime(DateTime? utc) => utc.HasValue ? DateAndTime(utc.Value) : null;

        // תאריך מקומי בלי שעה: "יום שישי 04/12"
        public static string Date(DateTime localDate) =>
            $"יום {DayNames[(int)localDate.DayOfWeek]} {localDate.ToString("dd/MM", CultureInfo.InvariantCulture)}";

        // 13:30:00 -> "13:30"
        public static string Time(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        public static string? Time(TimeSpan? time) => time.HasValue ? Time(time.Value) : null;

        // יום בזמינות השבועית של מתנדב: 0 = ראשון ... 6 = מוצאי שבת
        public static string AvailabilityDay(int day) =>
            day == 6 ? "מוצאי שבת" : day is >= 0 and < 6 ? DayNames[day] : day.ToString(CultureInfo.InvariantCulture);

        public static string Donation(string? status) => Translate(DonationStatuses, status);
        public static string Match(string? status) => Translate(MatchStatuses, status);
        public static string Route(string? status) => Translate(RouteStatuses, status);

        private static string Translate(IReadOnlyDictionary<string, string> labels, string? status) =>
            status != null && labels.TryGetValue(status, out var label) ? label : status ?? "לא ידוע";

        private static readonly Dictionary<string, string> DonationStatuses = new()
        {
            ["Pending"] = "ממתינה לשיבוץ",
            ["Matched"] = "שובצה למתנדב",
            ["PickedUp"] = "נאספה - בדרך לעמותה",
            ["Delivered"] = "נמסרה - ממתינה לאישור העמותה",
            ["Completed"] = "הושלמה",
            ["Expired"] = "פג תוקף",
            ["Cancelled"] = "בוטלה",
        };

        private static readonly Dictionary<string, string> MatchStatuses = new()
        {
            ["AwaitingVolunteerConfirmation"] = "ממתין לאישור המתנדב",
            ["Confirmed"] = "המתנדב אישר",
            ["InPickup"] = "בדרך לאיסוף",
            ["InTransit"] = "נאסף - בדרך לעמותה",
            ["TimedOut"] = "פג זמן התגובה",
            ["Completed"] = "נמסר",
            ["Declined"] = "נדחה",
            ["NoShow"] = "המתנדב לא הגיע (No-Show)",
            ["Cancelled"] = "בוטל",
        };

        private static readonly Dictionary<string, string> RouteStatuses = new()
        {
            ["Planned"] = "מתוכנן - עוד לא יצא לדרך",
            ["InProgress"] = "בביצוע",
            ["Completed"] = "הושלם",
            ["Cancelled"] = "בוטל",
        };
    }
}
