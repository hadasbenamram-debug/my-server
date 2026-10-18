using DAL.Classes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DAL.Classes
{
    public class Supplier
    {
        [Key]
        public int Id { get; set; } // מפתח ראשי עצמאי לטבלת הספקים (Identity / Auto-increment)

        // הגדרת מזהה המשתמש כמפתח זר נפרד
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required, MaxLength(100)]
        public string BusinessName { get; set; }

        [Required]
        public string Address { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [MaxLength(255)]
        public string GooglePlaceId { get; set; }

        [Required, MaxLength(20)]
        public string TaxId { get; set; }
        [Required] public string BusinessLicenseFilePath { get; set; }
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;

        public ICollection<Donation> Donations { get; set; }
    }
}