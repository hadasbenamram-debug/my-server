using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת לוג משלוח ע"י המשתמש/המתנדב (התאמשיות ומזהים יוכנסו בצורה מאובטחת בשרת)
    public class CreateDeliveryLogDto
    {
        [Required(ErrorMessage = "יש לציין מזהה מסלול מתנדב.")]
        public int VolunteerRouteId { get; set; }

        [Required(ErrorMessage = "סטטוס הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "הסטטוס יכול להכיל עד 50 תווים.")]
        public string Status { get; set; }

        [StringLength(500, ErrorMessage = "ההערות יכולות להכיל עד 500 תווים.")]
        public string Notes { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double? Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double? Longitude { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class DeliveryLogResponseDto
    {
        public int Id { get; set; }
        public int VolunteerRouteId { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public DateTime Timestamp { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}