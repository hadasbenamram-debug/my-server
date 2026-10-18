using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת משבצת זמינות ע"י המתנדב (ללא חשיפת/קבלת מזהה מתנדב פגיע מהקליינט)
    public class CreateAvailabilitySlotDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור אזור מוצא.")]
        public int RegionId { get; set; }

        // אזור יעד להובלה בין-אזורית. null / 0 = זמינות רגילה באזור המוצא בלבד.
        // אם מולא - חייב להיות שונה מאזור המוצא.
        public int? ToRegionId { get; set; }

        [Range(0, 6, ErrorMessage = "יום בשבוע חייב להיות בין 0 (ראשון) ל-6 (שבת).")]
        public int Day { get; set; }

        [Required(ErrorMessage = "יש לציין שעת התחלה.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?$", ErrorMessage = "פורמט שעת התחלה חייב להיות HH:mm.")]
        public string StartTime { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש לציין שעת סיום.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?$", ErrorMessage = "פורמט שעת סיום חייב להיות HH:mm.")]
        public string EndTime { get; set; } = string.Empty;
    }

    // משבצת זמינות כפי שהיא חוזרת ללקוח
    public class AvailabilitySlotResponseDto
    {
        public int Id { get; set; }
        public int VolunteerId { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public int? ToRegionId { get; set; }
        public string? ToRegionName { get; set; }
        public int Day { get; set; }

        // "HH:mm"
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }
}
