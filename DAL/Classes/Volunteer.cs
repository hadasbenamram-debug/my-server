using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DAL.Classes
{
    public class Volunteer
    {
        [Key]
        public int Id { get; set; } // מפתח ראשי עצמאי לטבלת המתנדבים (Identity / Auto-increment)

        // הגדרת מזהה המשתמש כמפתח זר נפרד
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required, MaxLength(50)]
        public string VehicleType { get; set; }

        // שים לב: קיבולת נמדדת בק"ג לצורך אחידות עם Donation.WeightKg
        // (בעבר הושוותה בטעות מול Quantity ב-BacktrackingAssignmentService).
        [Required]
        public double MaxCapacityKg { get; set; }

        public bool IsAvailable { get; set; } = true;

        // מיקום נוכחי - מתעדכן בזמן אמת דרך שיתוף מיקום GPS מהאפליקציה/דפדפן.
        public double CurrentLatitude { get; set; }
        public double CurrentLongitude { get; set; }
        public DateTime? LocationUpdatedAt { get; set; }

        [Required, MaxLength(15)]
        public string IdNumber { get; set; }
        [Required, MaxLength(20)] public string DriverLicenseNumber { get; set; }
        public DateTime? DriverLicenseExpiry { get; set; }

        [Required]
        public DateTime DateOfBirth { get; set; }

        // תוקן: הסקאלה הרשמית 1-5 (תואם Feedback.VolunteerRating), ברירת המחדל שונתה
        // מ-5.0 (=מקסימום, לא "ניטרלי") ל-3.5 (אמצע הסקאלה) כדי לא להעדיף מתנדבים
        // חדשים על פני ותיקים אמינים.
        public double ReliabilityScore { get; set; } = 3.5; // סקאלה 1-5

        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;

        public ICollection<Match> Deliveries { get; set; }
        public ICollection<AvailabilitySlot> AvailabilitySlots { get; set; }
        public ICollection<VolunteerRoute> Routes { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}