using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class UpdateNgoHoursDto
    {
        // כתובת מלאה. המיקום המדויק (קו רוחב ואורך) נקבע בשרת לפי הכתובת, דרך Google Maps.
        [Required(ErrorMessage = "יש להזין כתובת.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        public TimeSpan? SunToThuOpenTime { get; set; }
        public TimeSpan? SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
    }
}
