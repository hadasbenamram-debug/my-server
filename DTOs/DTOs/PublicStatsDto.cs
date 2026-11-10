using System;

namespace FoodRescue.DTOs
{
    // תיקון 31: נתוני "ההשפעה שלנו" בסוף דף הבית (GET api/stats/public, פתוח לכולם).
    // בעבר המספרים היו קבועים בקוד הלקוח (306 עסקים, 1,020 מתנדבים, 12,750 ארוחות). עכשיו הכול נספר מהמסד.
    // רק מספרים מצטברים - בלי שום פרט מזהה על משתמש, ספק או עמותה.
    public class PublicStatsDto
    {
        // תרומות שהגיעו לעמותה (המתנדב מסר / העמותה אישרה קבלה), מאז שהמערכת התחילה לפעול
        public int DonationsDelivered { get; set; }

        // ק"ג מזון שהגיעו לעמותות, מאז שהמערכת התחילה לפעול
        public double KgRescued { get; set; }

        // ק"ג מזון שהגיעו לעמותות מתחילת החודש הנוכחי (לפי שעון ישראל)
        public double KgRescuedThisMonth { get; set; }

        // ספקים (עסקים) מאושרים
        public int PartnerBusinesses { get; set; }

        // מתנדבים מאושרים
        public int ActiveVolunteers { get; set; }

        // עמותות מאושרות
        public int PartnerNgos { get; set; }

        // מתי חושבו המספרים (UTC). השרת שומר אותם במטמון לכמה דקות.
        public DateTime GeneratedAt { get; set; }
    }
}
