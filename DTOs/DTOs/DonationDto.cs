using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO לשורת קטגוריה בתרומה
    public class DonationCategoryLineDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "יש לציין מזהה קטגוריה.")]
        public int CategoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "הכמות חייבת להיות לפחות 1.")]
        public int Quantity { get; set; }

        [Range(0.01, 10000, ErrorMessage = "המשקל חייב להיות גדול מאפס.")]
        public double WeightKg { get; set; }

        // מוחזר ללקוח בלבד (שם הקטגוריה)
        public string? CategoryName { get; set; }
    }

    // DTO ליצירת תרומה. אין בו SupplierId: הספק נקבע בשרת לפי הטוקן (או לפי הנתיב, כשמנהל יוצר בשם ספק).
    // אין בו גם PriorityScore, NgoId או סטטוסים - כל אלה נקבעים במערכת.
    // אין בו מיקום: מקום האיסוף הוא המיקום המדויק של העסק, שהשרת קבע לפי הכתובת (Google Maps).
    public class CreateDonationDto
    {
        // תיקון 33: הספק כבר לא בוחר אזור. מקום האיסוף הוא הכתובת של העסק מההרשמה, והאזור נקבע בשרת לפי המיקום
        // שלה (RegionLocator). השדה נשאר רק לתאימות: ספק שולח אותו - הוא לא משפיע; מנהל שיוצר תרומה בשם ספק
        // יכול לקבוע אזור אחר במפורש.
        [Range(1, int.MaxValue, ErrorMessage = "האזור שנבחר אינו תקין.")]
        public int? RegionId { get; set; }

        // שם קובץ התמונה שהתקבל מ-analyze-image (רק שם, בלי נתיב)
        [StringLength(100, ErrorMessage = "שם קובץ התמונה ארוך מדי.")]
        public string? Image { get; set; }

        // שעת האיסוף האחרונה היום (שעון מקומי)
        public TimeSpan LatestPickupTime { get; set; }

        // תיקון 43: "זמינה לאיסוף החל מ-" היום (שעון מקומי). לא נשלח = אפשר לאסוף מיד.
        // חייבת להיות לפחות 30 דקות לפני שעת האיסוף האחרונה.
        public TimeSpan? EarliestPickupTime { get; set; }

        // תאריך התפוגה (המוקדם מבין המוצרים). אם לא נשלח - סוף היום המקומי.
        // ערך בלי אזור זמן מתפרש כשעון מקומי.
        public DateTime? ExpirationTime { get; set; }

        [MinLength(1, ErrorMessage = "יש לספק לפחות שורת קטגוריה אחת עבור התרומה.")]
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();
    }

    // עדכון תרומה ע"י הספק - אפשרי רק כל עוד התרומה ממתינה לשיבוץ (Pending)
    public class UpdateDonationDto
    {
        public TimeSpan LatestPickupTime { get; set; }

        // תיקון 43: "זמינה לאיסוף החל מ-" (null = אפשר לאסוף מיד)
        public TimeSpan? EarliestPickupTime { get; set; }

        public DateTime? ExpirationTime { get; set; }

        [MinLength(1, ErrorMessage = "יש לספק לפחות שורת קטגוריה אחת עבור התרומה.")]
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();
    }

    // ביטול תרומה (סיבה אופציונלית)
    public class CancelDonationDto
    {
        [StringLength(300, ErrorMessage = "הסיבה יכולה להכיל עד 300 תווים.")]
        public string? Reason { get; set; }
    }

    // תרומה כפי שהיא חוזרת ללקוח
    public class DonationResponseDto
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public int RegionId { get; set; }
        public string? RegionName { get; set; }
        public string Status { get; set; } = string.Empty;
        public double PriorityScore { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? GooglePlaceId { get; set; }
        public string? Image { get; set; }   // שם הקובץ בלבד. כתובת התמונה: /images/ + Image
        public TimeSpan LatestPickupTime { get; set; }
        public TimeSpan? EarliestPickupTime { get; set; }   // תיקון 43: "זמינה לאיסוף החל מ-" (null = מיד)
        public DateTime ExpirationTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public double TotalWeightKg { get; set; }
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();

        // פרטי השידוך הנוכחי (אם יש) - ממולא בתשובות לעמותה / לספק / למנהל
        public int? MatchId { get; set; }
        public string? MatchStatus { get; set; }
        public int? NgoId { get; set; }
        public string? NgoName { get; set; }
        public int? VolunteerId { get; set; }
        public string? VolunteerName { get; set; }
        public DateTime? EstimatedArrival { get; set; }

        // המסלול של השידוך - למעקב החי (api/tracking/routes/{id}). null = עוד אין מסלול
        public int? VolunteerRouteId { get; set; }

        // תיקון 60: מועדי השלבים של השידוך - לציר "מעקב תרומה" (null = השלב עוד לא קרה)
        public DateTime? MatchAssignedAt { get; set; }
        public DateTime? VolunteerConfirmedAt { get; set; }
        public DateTime? PickedUpAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }
}
