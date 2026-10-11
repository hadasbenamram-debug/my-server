using System;
using System.ComponentModel.DataAnnotations;
using DAL.Classes; // <-- הוספת הספרייה הזו פותרת את שגיאת ה-ApprovalStatus

namespace FoodRescue.DTOs
{
    public class CreateVolunteerDto
    {
        [Required(ErrorMessage = "סוג הרכב הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "סוג הרכב יכול להכיל עד 50 תווים.")]
        public string VehicleType { get; set; }

        [Range(0.1, 10000, ErrorMessage = "קיבולת משקל מקסימלית חייבת להיות גדולה מאפס.")]
        public double MaxCapacityKg { get; set; }

        public bool IsAvailable { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב נוכחי חייב להיות בין -90 ל-90.")]
        public double CurrentLatitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך נוכחי חייב להיות בין -180 ל-180.")]
        public double CurrentLongitude { get; set; }

        [StringLength(20, ErrorMessage = "מספר תעודת זהות תקין מכיל עד 20 תווים.")]
        public string IdNumber { get; set; }

        [DataType(DataType.Date)]
        public DateTime DateOfBirth { get; set; }

        [Required(ErrorMessage = "מספר רישיון נהיגה הוא שדה חובה.")]
        [StringLength(20, ErrorMessage = "מספר רישיון נהיגה יכול להכיל עד 20 תווים.")]
        public string DriverLicenseNumber { get; set; }

        public DateTime? DriverLicenseExpiry { get; set; }
    }

    public class VolunteerResponseDto
    {
        public int Id { get; set; }
        public string VehicleType { get; set; }
        public double MaxCapacityKg { get; set; }
        public bool IsAvailable { get; set; }
        public double CurrentLatitude { get; set; }
        public double CurrentLongitude { get; set; }
        public string IdNumber { get; set; }
        public DateTime DateOfBirth { get; set; }
        public double ReliabilityScore { get; set; }
        public ApprovalStatus ApprovalStatus { get; set; }

        // הוסף את השדות האלו כדי שהם יגיעו לפרונטאנד:
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }
}