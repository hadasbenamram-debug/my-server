using System;

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
    }

    // תוצאת ניתוח תמונת תרומה (POST api/Donations/analyze-image)
    public class ImageAnalysisResponseDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string? AiParsedData { get; set; }
        public string? Message { get; set; }
    }
}
