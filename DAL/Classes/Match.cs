using DAL.Classes;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public class Match
    {
        [Key]
        public int Id { get; set; }

        public int DonationId { get; set; }
        public Donation Donation { get; set; }

        public int NgoId { get; set; }
        public Ngo Ngo { get; set; }

        public int VolunteerId { get; set; }
        public Volunteer Volunteer { get; set; }

        public int? VolunteerRouteId { get; set; }
        public VolunteerRoute VolunteerRoute { get; set; }

        [Required] public MatchStatus Status { get; set; } = MatchStatus.AwaitingVolunteerConfirmation;

        // נוסף: מסמן איזו רשומת Match היא הניסיון הפעיל הנוכחי, לעומת ניסיונות
        // קודמים (Declined/NoShow) שנשמרים כהיסטוריה. לכל תרומה - Match אחד
        // עם IsActive=true בו-זמנית לכל היותר (לאכוף ב-BLL).
        public bool IsActive { get; set; } = true;

        public double MatchScore { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EstimatedArrival { get; set; }
        public DateTime? ResponseDeadline { get; set; }

        // נוסף: מרחק/משך נסיעה בפועל כפי שחושבו ע"י Google Distance Matrix API
        // בזמן השידוך - לשקיפות/דיבוג, וכדי לא להסתמך על ערך שמשתנה בזמן אמת.
        public double? GoogleMapsDistanceKm { get; set; }
        public double? GoogleMapsDurationMinutes { get; set; }

        public Feedback Feedback { get; set; }
        public ICollection<Comment> Comments { get; set; }
    }
}