using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // שורה ברשימת התרומות של הספק המחובר (GET api/Donations/my-supplier-donations)
    public class SupplierDonationSummaryDto
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public double PriorityScore { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpirationTime { get; set; }
        public TimeSpan LatestPickupTime { get; set; }
        public TimeSpan? EarliestPickupTime { get; set; }   // תיקון 43: "זמינה לאיסוף החל מ-" (null = מיד)
        public string? Image { get; set; }   // שם קובץ התמונה בלבד

        // תיקון 48: פרטים לדשבורד של הספק - מה נתרם, ומי מטפל בתרומה עכשיו (השידוך האחרון שלה)
        public double TotalWeightKg { get; set; }
        public List<DonationCategoryLineDto> Categories { get; set; } = new List<DonationCategoryLineDto>();
        public DateTime? ReceivedAt { get; set; }
        public string? MatchStatus { get; set; }
        public string? NgoName { get; set; }
        public string? VolunteerName { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public int? VolunteerRouteId { get; set; }
    }

    // שורה ברשימת כל התרומות למנהל (GET api/Donations)
    public class AdminDonationSummaryDto
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpirationTime { get; set; }
        public double UrgencyScore { get; set; }
        public double PriorityScore { get; set; }
        public string? Image { get; set; }   // שם קובץ התמונה בלבד
    }

    // תוצאת ניתוח תמונת תרומה (POST api/Donations/analyze-image)
    public class ImageAnalysisResponseDto
    {
        public string Image { get; set; } = string.Empty;   // שם הקובץ בלבד
        public string? AiParsedData { get; set; }
        public string? Message { get; set; }
    }
}
