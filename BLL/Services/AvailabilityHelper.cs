using DAL;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// לוגיקת עזר משותפת לבדיקת AvailabilitySlot. כל הבדיקות מבוצעות בשעון מקומי (LocalClock).
    ///
    /// משמעות Slot לפי מסמך התיאור: RegionId = אזור שממנו אפשר להתחיל את הנסיעה,
    /// ToRegionId = אזור היעד שאליו אפשר להעביר תרומות (null = זמינות רגילה באזור אחד).
    /// </summary>
    public static class AvailabilityHelper
    {
        /// <summary>תומך גם בחלון שחוצה חצות (למשל 22:00-02:00).</summary>
        public static bool IsWithinTimeWindow(AvailabilitySlot slot, TimeSpan currentTime)
        {
            if (slot.StartTime <= slot.EndTime)
                return currentTime >= slot.StartTime && currentTime <= slot.EndTime;

            return currentTime >= slot.StartTime || currentTime <= slot.EndTime;
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

        /// <summary>האם למתנדב יש Slot פעיל ברגע asOfUtc (יום + שעה מקומיים) שמתאים לתרומה ולעמותה.</summary>
        public static bool CanServeAt(Volunteer volunteer, DateTime asOfUtc, int donationRegionId, int ngoRegionId)
        {
            if (volunteer.AvailabilitySlots == null) return false;

            var local = LocalClock.ToLocal(asOfUtc);
            var regions = new[] { donationRegionId };

            return volunteer.AvailabilitySlots.Any(slot =>
                slot.Day == local.DayOfWeek &&
                IsWithinTimeWindow(slot, local.TimeOfDay) &&
                SlotServesRoute(slot, regions, ngoRegionId));
        }

        /// <summary>
        /// האם Slot אחד מכסה את כל המסלול: שעת היציאה ושעת ההגעה לעמותה בתוך החלון,
        /// והאזורים מתאימים. תומך במסלול שחוצה חצות בתוך Slot שחוצה חצות.
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

            var start = LocalClock.ToLocal(routeStartUtc);
            var end = LocalClock.ToLocal(routeEndUtc);

            foreach (var slot in volunteer.AvailabilitySlots)
            {
                if (slot.Day != start.DayOfWeek) continue;
                if (!SlotServesRoute(slot, donationRegionIds, ngoRegionId)) continue;
                if (!IsWithinTimeWindow(slot, start.TimeOfDay)) continue;

                if (end.Date == start.Date)
                {
                    if (IsWithinTimeWindow(slot, end.TimeOfDay)) return true;
                }
                else if (slot.StartTime > slot.EndTime &&
                         end.Date == start.Date.AddDays(1) &&
                         end.TimeOfDay <= slot.EndTime)
                {
                    // ה-Slot חוצה חצות והסיום נופל בזנב שלו, ביום המחרת
                    return true;
                }
            }

            return false;
        }

        /// <summary>בדיקה בין-אזורית מפורשת: Slot שמכריז מעבר מ-fromRegionId ל-toRegionId ופתוח ברגע asOf (UTC).</summary>
        public static bool HasCrossRegionAvailability(Volunteer volunteer, int fromRegionId, int toRegionId, DateTime asOf)
        {
            if (volunteer.AvailabilitySlots == null) return false;

            var local = LocalClock.ToLocal(asOf);

            return volunteer.AvailabilitySlots.Any(slot =>
                slot.RegionId == fromRegionId &&
                slot.ToRegionId == toRegionId &&
                slot.Day == local.DayOfWeek &&
                IsWithinTimeWindow(slot, local.TimeOfDay));
        }
    }
}
