using BLL.Algorithms;
using DAL.Classes;
using System;

namespace BLL.Services
{
    /// <summary>
    /// תיקון 70: ההגדרה היחידה ל"מקרה No-Show שאפשר עדיין לטפל בו". משמשת גם את לוח "מי מאחר" (כפתור השיבוץ)
    /// וגם את המונה בלוח הבקרה של המנהל - כדי שהמספר שם תמיד יתאים למה שאפשר לעשות במסך הטיפול.
    /// אפשר לטפל כשהתרומה עדיין ממתינה לשיבוץ (לא שובצה מחדש, לא בוטלה ולא פג תוקפה), שעת האיסוף האחרונה
    /// שלה עוד לא עברה (וממילא היא לפני התפוגה), והיא נוצרה היום. מקרה שלא עומד בזה לא נמחק מהמסך - הוא רק לא נספר.
    /// </summary>
    public static class NoShowHandling
    {
        public static bool CanBeHandled(Donation? donation, DateTime nowUtc) =>
            donation != null &&
            donation.Status == DonationStatus.Pending &&
            NgoAvailabilityHelper.GetPickupDateTime(donation) > nowUtc &&
            LocalClock.ToLocal(donation.CreatedAt).Date == LocalClock.ToLocal(nowUtc).Date;
    }
}
