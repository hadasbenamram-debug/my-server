using BLL.Algorithms;
using BLL.Configuration;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Agent
{
    public sealed record ShabbatTimes(string Starts, string Ends, bool IsShabbatNow, bool SystemPausesOnShabbat);

    public sealed record HolidayInfo(string Date, string Name, bool IsYomTov);

    // המבנה של התשובה מ-Hebcal: רק השדות שצריך (שדות שאין במחלקה פשוט לא נקראים)
    public sealed class HebcalResponse
    {
        public List<HebcalItem> Items { get; set; } = new();
    }

    public sealed class HebcalItem
    {
        public string Date { get; set; } = "";
        public string Hebrew { get; set; } = "";
        public string Category { get; set; } = "";
        public bool YomTov { get; set; }   // "yomtov": true - יום טוב (בלי מלאכה)
    }

    // כלי לוח שנה - לכל התפקידים:
    //  * שבת: מהחישוב של המערכת עצמה (ShabbatClock) - בדיוק הזמנים שלפיהם מנוע השידוכים עוצר בשבת.
    //  * חגים: מ-Hebcal (https://www.hebcal.com), שירות לוח שנה עברי חינמי שלא צריך מפתח. זה ה-API החיצוני של ה-Agent.
    // המערכת עוצרת בשבת אבל לא מכירה את החגים - והכלי הזה משלים את המידע לפני חג (עמותות סגורות, איסופים מוקדמים).
    // רשום ב-Program.cs עם AddHttpClient, שמזריק HttpClient עם הכתובת הבסיסית של Hebcal.
    public sealed class CalendarTools
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;
        private readonly IMatchingPolicyProvider _policy;
        private readonly TimeProvider _clock;

        public CalendarTools(HttpClient http, IMemoryCache cache, IMatchingPolicyProvider policy, TimeProvider clock)
        {
            _http = http;
            _cache = cache;
            _policy = policy;
            _clock = clock;
        }

        // כלי סינכרוני: רק חישוב, בלי המתנה לשום דבר
        [Description("מחזיר מתי נכנסת ומתי יוצאת השבת הקרובה (או הנוכחית), והאם עכשיו שבת. " +
                     "בשבת המערכת לא שולחת הצעות למתנדבים ולא משבצת תרומות - הן ממתינות לצאת השבת.")]
        public ShabbatTimes GetShabbatTimes()
        {
            var policy = _policy.Current;
            var now = _clock.GetUtcNow().UtcDateTime;
            var window = ShabbatClock.CurrentOrNext(now, policy);

            return new ShabbatTimes(
                AgentFormat.DateAndTime(window.StartUtc),
                AgentFormat.DateAndTime(window.EndUtc),
                ShabbatClock.IsShabbat(now, policy),
                policy.ObserveShabbat);
        }

        // כלי אסינכרוני: פנייה לשרת אחר לוקחת זמן, ולכן HttpClient עם await.
        // ה-CancellationToken לא נשלח למודל כפרמטר - הספרייה מעבירה את ה-Token של הבקשה.
        [Description("מחזיר את החגים בימים הקרובים לפי לוח השנה בישראל: תאריך, שם החג בעברית, והאם זה יום טוב (יום שבו לא עושים מלאכה, " +
                     "ולכן בדרך כלל אין איסופים ומסירות ועמותות סגורות). כולל גם ערבי חג.")]
        public async Task<List<HolidayInfo>> GetUpcomingHolidaysAsync(
            [Description("כמה ימים קדימה לבדוק, בין 7 ל-120. אם המשתמש לא ביקש טווח - 60")] int days,
            CancellationToken cancellationToken = default)
        {
            days = Math.Clamp(days, 7, 120);
            var start = LocalClock.ToLocal(_clock.GetUtcNow().UtcDateTime).Date;
            var end = start.AddDays(days);

            // לוח השנה לא משתנה - שומרים במטמון ולא פונים ל-Hebcal בכל שאלה
            var cacheKey = $"agent:hebcal:{start:yyyy-MM-dd}:{days}";
            if (_cache.TryGetValue(cacheKey, out List<HolidayInfo>? cached) && cached != null)
                return cached;

            // maj=on: חגים עיקריים בלבד. i=on: לוח החגים של ישראל (בלי יום טוב שני של גלויות)
            var url = "hebcal?v=1&cfg=json&maj=on&min=off&mod=off&nx=off&mf=off&ss=off&c=off&s=off&i=on" +
                      $"&start={start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                      $"&end={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

            var response = await _http.GetFromJsonAsync<HebcalResponse>(url, cancellationToken);

            // מחזירים רק תאריך, שם והאם יום טוב - לא את כל התשובה של Hebcal
            var holidays = (response?.Items ?? new List<HebcalItem>())
                .Where(i => i.Category == "holiday" && !string.IsNullOrWhiteSpace(i.Hebrew))
                .Select(i => new HolidayInfo(FormatDate(i.Date), i.Hebrew, i.YomTov))
                .ToList();

            _cache.Set(cacheKey, holidays, CacheDuration);
            return holidays;
        }

        // Hebcal מחזיר "2026-12-05" (או תאריך עם שעה) - מציגים "יום שבת 05/12"
        private static string FormatDate(string value) =>
            value.Length >= 10 && DateTime.TryParseExact(value.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? AgentFormat.Date(date)
                : value;
    }
}
