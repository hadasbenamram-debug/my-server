using DAL;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// הופעה בפועל של משבצת זמינות בתאריך מסוים (UTC). משבצת שחוצה חצות (למשל 22:00-02:00) נמשכת אל היום הבא.
    /// </summary>
    public readonly record struct AvailabilityWindow(AvailabilitySlot Slot, DateTime StartUtc, DateTime EndUtc)
    {
        public bool Contains(DateTime utc) => utc >= StartUtc && utc <= EndUtc;
        public bool Covers(DateTime fromUtc, DateTime toUtc) => fromUtc >= StartUtc && toUtc <= EndUtc;
    }

    /// <summary>
    /// לוגיקת עזר משותפת לבדיקת AvailabilitySlot. כל הבדיקות מבוצעות בשעון מקומי (LocalClock).
    ///
    /// משמעות Slot לפי מסמך התיאור: RegionId = אזור שממנו אפשר להתחיל את הנסיעה,
    /// ToRegionId = אזור היעד שאליו אפשר להעביר תרומות (null = זמינות רגילה באזור אחד).
    ///
    /// תיקון 28: הבדיקות הושוו בעבר ל"יום של היום" בלבד (slot.Day == היום). משבצת של יום שני 22:00-02:00
    /// לא זוהתה ביום שלישי ב-01:00 (באמצע המשמרת), ומנגד המשבצת של יום שלישי בלילה "נפתחה" כבר ב-01:00.
    /// עכשיו כל משבצת הופכת לחלון זמן אמיתי (GetWindows) - כולל ההמשך ביום שאחרי - וכל הבדיקות עוברות דרכו.
    /// זה גם מה שמאפשר למנוע לתכנן לפי מערכת השעות של המתנדב (משמרת שמתחילה בעוד חצי שעה - תיקון 24).
    /// </summary>
    public static class AvailabilityHelper
    {
        /// <summary>תומך גם בחלון שחוצה חצות (למשל 22:00-02:00). בודק שעה בלבד, בלי היום.</summary>
        public static bool IsWithinTimeWindow(AvailabilitySlot slot, TimeSpan currentTime)
        {
            if (slot.StartTime <= slot.EndTime)
                return currentTime >= slot.StartTime && currentTime <= slot.EndTime;

            return currentTime >= slot.StartTime || currentTime <= slot.EndTime;
        }

        /// <summary>
        /// כל החלונות בפועל של המשבצות של המתנדב שחופפים לטווח [fromUtc, toUtc], ממוינים לפי שעת ההתחלה.
        /// נבדק גם היום שלפני הטווח - בשביל משבצת לילה שהתחילה אתמול ונמשכת לתוך הטווח.
        /// </summary>
        public static List<AvailabilityWindow> GetWindows(Volunteer volunteer, DateTime fromUtc, DateTime toUtc)
        {
            var windows = new List<AvailabilityWindow>();
            if (volunteer?.AvailabilitySlots == null || volunteer.AvailabilitySlots.Count == 0 || toUtc < fromUtc)
                return windows;

            var firstDay = LocalClock.ToLocal(fromUtc).Date.AddDays(-1);
            var lastDay = LocalClock.ToLocal(toUtc).Date;

            for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                foreach (var slot in volunteer.AvailabilitySlots)
                {
                    if (slot.Day != day.DayOfWeek) continue;

                    var localStart = day + slot.StartTime;
                    var localEnd = slot.StartTime <= slot.EndTime ? day + slot.EndTime : day.AddDays(1) + slot.EndTime;
                    var window = new AvailabilityWindow(slot, LocalClock.ToUtc(localStart), LocalClock.ToUtc(localEnd));

                    if (window.EndUtc >= fromUtc && window.StartUtc <= toUtc)
                        windows.Add(window);
                }
            }

            windows.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            return windows;
        }

        /// <summary>
        /// האם ה-Slot מתאים לנסיעה שאוספת מאזורי donationRegionIds ומוסרת לעמותה באזור ngoRegionId.
        /// תיקון: הקוד המקורי לא בדק את אזור העמותה כשכל התרומות היו באזור המוצא, ולכן מתנדב
        /// שלא הצהיר על נסיעה בין-אזורית יכול היה לקבל מסלול לעמותה באזור אחר.
        /// </summary>
        public static bool SlotServesRoute(AvailabilitySlot slot, IReadOnlyCollection<int> donationRegionIds, int ngoRegionId)
        {
            if (donationRegionIds.Count == 0) return false;

            // כל האיסופים באזור המוצא; היעד: אותו אזור (זמינות רגילה) או אזור היעד שהוצהר
            bool allFromOrigin = donationRegionIds.All(r => r == slot.RegionId);
            if (allFromOrigin && (ngoRegionId == slot.RegionId || slot.ToRegionId == ngoRegionId))
                return true;

            // נסיעה בין-אזורית מוצהרת: איסופים באזור המוצא או באזור היעד, והעמותה באזור היעד
            if (slot.ToRegionId.HasValue && ngoRegionId == slot.ToRegionId.Value &&
                donationRegionIds.All(r => r == slot.RegionId || r == slot.ToRegionId.Value))
                return true;

            return false;
        }

        /// <summary>האם למתנדב יש Slot פעיל ברגע asOfUtc שמתאים לתרומה ולעמותה.</summary>
        public static bool CanServeAt(Volunteer volunteer, DateTime asOfUtc, int donationRegionId, int ngoRegionId)
        {
            var regions = new[] { donationRegionId };
            return GetWindows(volunteer, asOfUtc, asOfUtc)
                .Any(w => w.Contains(asOfUtc) && SlotServesRoute(w.Slot, regions, ngoRegionId));
        }

        /// <summary>
        /// האם חלון זמינות אחד מכסה את כל המסלול (מהיציאה ועד ההגעה לעמותה), והאזורים מתאימים.
        /// מסלול שחוצה חצות בתוך משבצת לילה - נתמך (החלון נמשך ליום שאחרי).
        /// </summary>
        public static bool CoversRoute(
            Volunteer volunteer,
            DateTime routeStartUtc,
            DateTime routeEndUtc,
            IReadOnlyCollection<int> donationRegionIds,
            int ngoRegionId)
        {
            if (volunteer?.AvailabilitySlots == null || donationRegionIds == null || donationRegionIds.Count == 0)
                return false;

            return GetWindows(volunteer, routeStartUtc, routeEndUtc)
                .Any(w => w.Covers(routeStartUtc, routeEndUtc) && SlotServesRoute(w.Slot, donationRegionIds, ngoRegionId));
        }

        /// <summary>בדיקה בין-אזורית מפורשת: Slot שמכריז מעבר מ-fromRegionId ל-toRegionId ופתוח ברגע asOf (UTC).</summary>
        public static bool HasCrossRegionAvailability(Volunteer volunteer, int fromRegionId, int toRegionId, DateTime asOf)
        {
            return GetWindows(volunteer, asOf, asOf)
                .Any(w => w.Contains(asOf) && w.Slot.RegionId == fromRegionId && w.Slot.ToRegionId == toRegionId);
        }
    }
}
