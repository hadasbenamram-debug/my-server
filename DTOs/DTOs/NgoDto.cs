using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class CreateNgoDto
    {
        [Required(ErrorMessage = "שם העמותה הוא שדה חובה.")]
        [StringLength(150, ErrorMessage = "שם העמותה יכול להכיל עד 150 תווים.")]
        public string NgoName { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "קיבולת יומית חייבת להיות מספר חיובי.")]
        public int DailyCapacity { get; set; }

        [Required(ErrorMessage = "כתובת העמותה היא שדה חובה.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [StringLength(50, ErrorMessage = "מספר רישום (עמותה/חברה) יכול להכיל עד 50 תווים.")]
        public string RegistrationNumber { get; set; }

        [Required(ErrorMessage = "יש לבחור אזור.")]
        public int RegionId { get; set; }

        [Required(ErrorMessage = "שעת פתיחה (ראשון–חמישי) היא שדה חובה.")]
        public TimeSpan SunToThuOpenTime { get; set; }

        [Required(ErrorMessage = "שעת סגירה (ראשון–חמישי) היא שדה חובה.")]
        public TimeSpan SunToThuCloseTime { get; set; }

        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
    }

    public class NgoResponseDto
    {
        public int Id { get; set; }
        public string NgoName { get; set; }
        public int DailyCapacity { get; set; }
        public string Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string RegistrationNumber { get; set; }
        public TimeSpan SunToThuOpenTime { get; set; }
        public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
        public int ApprovalStatus { get; set; }
    }
}