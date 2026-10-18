using DAL.Classes;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic; // נדרש עבור ICollection ו-List

namespace DAL.Classes
{
    public class VolunteerRoute
    {
        [Key]
        public int Id { get; set; }
        public int VolunteerId { get; set; }
        public Volunteer Volunteer { get; set; }

        public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;
        public double TotalWeightKg { get; set; }

        [Required]
        public RouteStatus Status { get; set; } = RouteStatus.Planned;

        // מסלול מקודד מ-Google Routes API (null אם אין מפתח Google / הקריאה נכשלה)
        public string? GoogleDirectionsPolyline { get; set; }
        public double? EstimatedTotalDurationMinutes { get; set; }
        public double? EstimatedTotalDistanceKm { get; set; }

        // ציון ההתאמה (Matching Score) של המסלול כולו
        public double MatchScore { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ICollection<Match> Matches { get; set; }

        // הוספת אוסף הלוגים למימוש קשר One-to-Many
        public ICollection<DeliveryLog> DeliveryLogs { get; set; } = new List<DeliveryLog>();
    }
}