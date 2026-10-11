using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO לשורת קטגוריה בתרומה
    public class DonationCategoryLineDto
    {
        [Required(ErrorMessage = "יש לציין מזהה קטגוריה.")]
        public int CategoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "הכמות חייבת להיות לפחות 1.")]
        public int Quantity { get; set; }

        [Range(0.01, 10000, ErrorMessage = "המשקל חייב להיות גדול מאפס.")]
        public double WeightKg { get; set; }
    }

    // DTO ליצירת תרומה ע"י הספק (ללא PriorityScore שנקבע בשרת, וללא NgoId או סטטוסים שנקבעים במערכת)
    public class CreateDonationDto
    {
        // נשלח ע"י מנהל בלבד (יצירה בשם ספק). עבור ספק רגיל הערך מתעלמים ממנו והשרת קובע את הספק לפי ה-JWT.
        public int SupplierId { get; set; }

        [Required(ErrorMessage = "יש לציין מזהה אזור.")]
        public int RegionId { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [StringLength(200, ErrorMessage = "מזהה המקום בגוגל יכול להכיל עד 200 תווים.")]
        public string GooglePlaceId { get; set; }

        public TimeSpan LatestPickupTime { get; set; }

        [Required(ErrorMessage = "יש לציין תאריך תפוגה.")]
        public DateTime ExpirationTime { get; set; }

        [Required(ErrorMessage = "יש לספק לפחות שורת קטגוריה אחת עבור התרומה.")]
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();
    }

    // DTO מלא להצגה בשרת (כולל שדות מחושבים כמו PriorityScore וכו')
    public class DonationResponseDto
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public int RegionId { get; set; }
        public int NgoId { get; set; }
        public double PriorityScore { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string GooglePlaceId { get; set; }
        public TimeSpan LatestPickupTime { get; set; }
        public DateTime ExpirationTime { get; set; }
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();
    }
}