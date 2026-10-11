using DTOs.DTOs;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת משבצת זמינות ע"י המתנדב (ללא חשיפת/קבלת מזהה מתנדב פגיע מהקליינט)
    public class CreateAvailabilitySlotDto
    {
        [Required(ErrorMessage = "יש לציין אזור מוצא.")]
        public int RegionId { get; set; }

        [Required(ErrorMessage = "יש לציין אזור יעד.")]
        public int ToRegionId { get; set; }

        [Range(0, 6, ErrorMessage = "יום בשבוע חייב להיות בין 0 (ראשון) ל-6 (שבת).")]
        public int Day { get; set; }

        [Required(ErrorMessage = "יש לציין שעת התחלה.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "פורמט שעת התחלה חייב להיות HH:mm.")]
        public string StartTime { get; set; }

        [Required(ErrorMessage = "יש לציין שעת סיום.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "פורמט שעת סיום חייב להיות HH:mm.")]
        public string EndTime { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class AvailabilitySlotResponseDto
    {
        public int Id { get; set; }
        public int VolunteerId { get; set; }
        public string RegionName { get; set; }
        public int RegionId { get; set; }
        public CreateRegionDto Region { get; set; }
        public int ToRegionId { get; set; }
        public CreateRegionDto ToRegion { get; set; }
        public int Day { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }
}