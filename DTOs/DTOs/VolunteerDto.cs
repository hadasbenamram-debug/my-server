using FoodRescue.DTOs.Validation;
using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class CreateVolunteerDto
    {
        [Required(ErrorMessage = "סוג הרכב הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "סוג הרכב יכול להכיל עד 50 תווים.")]
        public string VehicleType { get; set; } = string.Empty;

        [Range(0.1, 10000, ErrorMessage = "קיבולת משקל מקסימלית חייבת להיות גדולה מאפס.")]
        public double MaxCapacityKg { get; set; }

        public bool IsAvailable { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב נוכחי חייב להיות בין -90 ל-90.")]
        public double CurrentLatitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך נוכחי חייב להיות בין -180 ל-180.")]
        public double CurrentLongitude { get; set; }

        // תוקן: היה עד 20 תווים בלי ספרת ביקורת, בזמן שהמסד מקבל עד 15
        [Required(ErrorMessage = "יש להזין מספר תעודת זהות.")]
        [StringLength(15, ErrorMessage = "מספר תעודת הזהות ארוך מדי.")]
        [IsraeliId]
        public string IdNumber { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        [MinimumAge(17, ErrorMessage = "מתנדב נהג חייב להיות בן 17 לפחות.")]
        public DateTime DateOfBirth { get; set; }

        [Required(ErrorMessage = "מספר רישיון נהיגה הוא שדה חובה.")]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "מספר רישיון הנהיגה חייב להכיל 5-20 תווים.")]
        public string DriverLicenseNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין את תאריך תוקף רישיון הנהיגה.")]
        [NotInPast(ErrorMessage = "תוקף רישיון הנהיגה פג.")]
        public DateTime? DriverLicenseExpiry { get; set; }
    }

    // DTO לעדכון פרופיל מתנדב.
    // רק שדות שהמתנדב רשאי לשנות. אין כאן Id, UserId, ApprovalStatus, ReliabilityScore או IdNumber -
    // אלה נקבעים בשרת בלבד (הגנה מפני mass assignment).
    public class UpdateVolunteerDto
    {
        [Required(ErrorMessage = "סוג הרכב הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "סוג הרכב יכול להכיל עד 50 תווים.")]
        public string VehicleType { get; set; } = string.Empty;

        [Range(0.1, 10000, ErrorMessage = "קיבולת משקל מקסימלית חייבת להיות גדולה מאפס.")]
        public double MaxCapacityKg { get; set; }

        public bool IsAvailable { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב נוכחי חייב להיות בין -90 ל-90.")]
        public double CurrentLatitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך נוכחי חייב להיות בין -180 ל-180.")]
        public double CurrentLongitude { get; set; }

        [NotInPast(ErrorMessage = "תוקף רישיון הנהיגה פג.")]
        public DateTime? DriverLicenseExpiry { get; set; }
    }

    public class VolunteerResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string VehicleType { get; set; } = string.Empty;
        public double MaxCapacityKg { get; set; }
        public bool IsAvailable { get; set; }
        public double CurrentLatitude { get; set; }
        public double CurrentLongitude { get; set; }
        public string IdNumber { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public DateTime? DriverLicenseExpiry { get; set; }
        public double ReliabilityScore { get; set; }
        public ApprovalStatus ApprovalStatus { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    // עדכון מיקום חי של המתנדב (גם דרך SignalR וגם ב-REST)
    public class LocationUpdateDto
    {
        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Range(-180, 180)]
        public double Longitude { get; set; }
    }
}
