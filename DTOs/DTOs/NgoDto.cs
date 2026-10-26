using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // Minimal view for approved volunteers: where to deliver and when. No RegistrationNumber, capacity or status.
    public class NgoPublicDto
    {
        public int Id { get; set; }
        public string NgoName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public TimeSpan SunToThuOpenTime { get; set; }
        public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
        public bool ReceivesOnSaturdayNight { get; set; }
        public TimeSpan? SaturdayNightOpenTime { get; set; }
        public TimeSpan? SaturdayNightCloseTime { get; set; }
    }

    public class NgoResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string NgoName { get; set; } = string.Empty;
        public int DailyCapacity { get; set; }
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int RegionId { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public TimeSpan SunToThuOpenTime { get; set; }
        public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
        public bool ReceivesOnSaturdayNight { get; set; }
        public TimeSpan? SaturdayNightOpenTime { get; set; }
        public TimeSpan? SaturdayNightCloseTime { get; set; }
        public int ApprovalStatus { get; set; }
    }

    // שדות פרופיל העמותה - משותפים להרשמה חדשה (RegisterNgoDto) ולהוספת תפקיד עמותה לחשבון קיים (CreateNgoDto)
    public interface INgoProfileFields
    {
        string NgoName { get; }
        int DailyCapacity { get; }
        string Address { get; }
        string RegistrationNumber { get; }
        int RegionId { get; }
        TimeSpan? SunToThuOpenTime { get; }
        TimeSpan? SunToThuCloseTime { get; }
        bool ReceivesOnFriday { get; }
        TimeSpan? FridayOpenTime { get; }
        TimeSpan? FridayCloseTime { get; }
        bool ReceivesOnSaturdayNight { get; }
        TimeSpan? SaturdayNightOpenTime { get; }
        TimeSpan? SaturdayNightCloseTime { get; }
    }

    // תיקון 10: משתמש מחובר מוסיף לחשבון שלו גם תפקיד עמותה (POST api/ngos). אותם שדות כמו בהרשמה, בלי פרטי המשתמש.
    public class CreateNgoDto : INgoProfileFields
    {
        [Required(ErrorMessage = "יש להזין את שם העמותה.")]
        [StringLength(100, ErrorMessage = "שם העמותה יכול להכיל עד 100 תווים.")]
        public string NgoName { get; set; } = string.Empty;

        [Range(1, 100000, ErrorMessage = "הקיבולת היומית חייבת להיות מספר חיובי.")]
        public int DailyCapacity { get; set; }

        // כתובת מלאה. המיקום המדויק (קו רוחב ואורך) נקבע בשרת לפי הכתובת, דרך Google Maps.
        [Required(ErrorMessage = "יש להזין את כתובת העמותה.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין מספר רישום (עמותה / חברה).")]
        [StringLength(20, ErrorMessage = "מספר הרישום יכול להכיל עד 20 תווים.")]
        public string RegistrationNumber { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור אזור.")]
        public int RegionId { get; set; }

        public TimeSpan? SunToThuOpenTime { get; set; }
        public TimeSpan? SunToThuCloseTime { get; set; }

        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }

        public bool ReceivesOnSaturdayNight { get; set; }
        public TimeSpan? SaturdayNightOpenTime { get; set; }
        public TimeSpan? SaturdayNightCloseTime { get; set; }
    }
}
