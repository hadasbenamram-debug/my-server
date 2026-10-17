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

    // DTO ליצירת תרומה ע"י הספק (ללא PriorityScore שנקבע בשרת, וללא NgoId או סטטוסים שנקבעים במערכת)
    public class CreateDonationDto
    {
        // נשלח ע"י מנהל בלבד (יצירה בשם ספק). עבור ספק רגיל הערך מתעלמים ממנו והשרת קובע את הספק לפי ה-JWT.
        public int SupplierId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור אזור.")]
        public int RegionId { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [StringLength(255, ErrorMessage = "מזהה המקום בגוגל יכול להכיל עד 255 תווים.")]
        public string? GooglePlaceId { get; set; }

        public string? ImageFilePath { get; set; }

        // שעת האיסוף האחרונה היום (שעון מקומי)
        public TimeSpan LatestPickupTime { get; set; }

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
        public string? ImageFilePath { get; set; }
        public TimeSpan LatestPickupTime { get; set; }
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
    }
}
