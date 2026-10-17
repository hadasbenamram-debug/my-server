using DAL.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public class Donation
    {
        [Key]
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; }

        public string? ImageFilePath { get; set; }
        public int RegionId { get; set; }
        public Region Region { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        // מזהה מקום ב-Google Places עבור נקודת האיסוף
        [MaxLength(255)]
        public string GooglePlaceId { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime ExpirationTime { get; set; }
        [Required] public TimeSpan LatestPickupTime { get; set; }
        public double PriorityScore { get; set; }
        public double UrgencyScore { get; set; }
        public DateTime PriorityUpdatedAt { get; set; } = DateTime.UtcNow;

        // Con avoid double-assignments
        [Timestamp]
        public byte[] RowVersion { get; set; }

        [Required]
        public DonationStatus Status { get; set; } = DonationStatus.Pending;

        // מתי העמותה אישרה שקיבלה את התרומה
        public DateTime? ReceivedAt { get; set; }

        // קשר טבלאות
        public ICollection<DonationFoodCategory> DonationFoodCategories { get; set; }
        public ICollection<Match> Matches { get; set; }
        public ICollection<Comment> Comments { get; set; }
    }
}