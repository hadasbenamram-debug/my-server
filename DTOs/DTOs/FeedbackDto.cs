using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת משוב ע"י המשתמש (ללא מזהה או תאריך שנקבעים באופן אוטומטי בשרת)
    public class CreateFeedbackDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "יש לציין מזהה שיבוץ (MatchId).")]
        public int MatchId { get; set; }

        [Range(1, 5, ErrorMessage = "הדירוג חייב להיות מספר בין 1 ל-5.")]
        public int Rating { get; set; }

        [Range(1, 5, ErrorMessage = "דירוג המתנדב חייב להיות מספר בין 1 ל-5.")]
        public int VolunteerRating { get; set; }

        [StringLength(500, ErrorMessage = "ההערות יכולות להכיל עד 500 תווים.")]
        public string? Comments { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class FeedbackResponseDto
    {
        public int Id { get; set; }
        public int MatchId { get; set; }
        public int Rating { get; set; }
        public int VolunteerRating { get; set; }
        public string? Comments { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? VolunteerId { get; set; }
        public string? VolunteerName { get; set; }
    }
}
