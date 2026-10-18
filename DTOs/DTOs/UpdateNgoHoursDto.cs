using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class UpdateNgoHoursDto
    {
        [Required(ErrorMessage = "יש להזין כתובת.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        public TimeSpan? SunToThuOpenTime { get; set; }
        public TimeSpan? SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
    }
}
