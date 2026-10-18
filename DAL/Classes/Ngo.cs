using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DAL.Classes
{
    public class Ngo
    {
        [Key]
        public int Id { get; set; } // מפתח ראשי עצמאי לטבלת ה-NGO (Identity / Auto-increment)

        // הגדרת מזהה המשתמש כמפתח זר נפרד
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required, MaxLength(100)]
        public string NgoName { get; set; }

        [Required]
        public int DailyCapacity { get; set; }

        [Required]
        public string Address { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [MaxLength(255)]
        public string GooglePlaceId { get; set; }
        [Required] public TimeSpan SunToThuOpenTime { get; set; }
        [Required] public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }

        [Required]
        public int RegionId { get; set; }

        [ForeignKey("RegionId")]
        public Region Region { get; set; }

        [Required, MaxLength(20)]
        public string RegistrationNumber { get; set; }

        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;

        public ICollection<Match> Matches { get; set; }
    }
}