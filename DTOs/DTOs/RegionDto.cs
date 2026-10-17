using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת אזור חדש (ללא מזהה שנקבע בצורה מאובטחת בשרת)
    public class CreateRegionDto
    {
        // תוקן: המסד מקבל עד 50 תווים (Region.RegionName)
        [Required(ErrorMessage = "שם האזור הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "שם האזור יכול להכיל עד 50 תווים.")]
        public string RegionName { get; set; } = string.Empty;

        [Range(-90, 90, ErrorMessage = "קו רוחב מרכזי חייב להיות בין -90 ל-90.")]
        public double CenterLatitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך מרכזי חייב להיות בין -180 ל-180.")]
        public double CenterLongitude { get; set; }

        [Range(0.1, 500, ErrorMessage = "רדיוס האזור בקילומטרים חייב להיות בטווח שבין 0.1 ל-500.")]
        public double RadiusKm { get; set; }
    }

    // אזור כפי שהוא חוזר ללקוח (כולל מזהה)
    public class RegionResponseDto
    {
        public int Id { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public double RadiusKm { get; set; }
    }
}
