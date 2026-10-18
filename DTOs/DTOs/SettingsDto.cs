using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // הגדרות המערכת שהמנהל יכול לשנות. נשמרות במסד ומשפיעות ישירות על מנוע השידוכים.
    // כל שדה null = "לא לשנות" (ב-PUT) / ערך ברירת המחדל (ב-GET תמיד מוחזר הערך בפועל).
    public class SystemSettingsDto
    {
        [Range(1, 120, ErrorMessage = "זמן התגובה למתנדב חייב להיות בין 1 ל-120 דקות.")]
        public int? MatchingTimeoutMinutes { get; set; }

        [Range(0, 1, ErrorMessage = "המשקל חייב להיות בין 0 ל-1.")]
        public double? DistanceWeight { get; set; }

        [Range(0, 1, ErrorMessage = "המשקל חייב להיות בין 0 ל-1.")]
        public double? UrgencyWeight { get; set; }

        [Range(0, 1, ErrorMessage = "המשקל חייב להיות בין 0 ל-1.")]
        public double? CapacityWeight { get; set; }

        [Range(0, 1, ErrorMessage = "המשקל חייב להיות בין 0 ל-1.")]
        public double? ReliabilityWeight { get; set; }

        [Range(1, 200, ErrorMessage = "רדיוס החיפוש חייב להיות בין 1 ל-200 ק\"מ.")]
        public double? MaxSearchRadiusKm { get; set; }

        [Range(0.5, 100, ErrorMessage = "רדיוס האשכול חייב להיות בין 0.5 ל-100 ק\"מ.")]
        public double? ClusterRadiusKm { get; set; }

        [Range(1, 50, ErrorMessage = "מספר המסלולים לבדיקה חייב להיות בין 1 ל-50.")]
        public int? MaxRoutesEvaluatedPerDonation { get; set; }

        [Range(0, 50, ErrorMessage = "המספר חייב להיות בין 0 ל-50.")]
        public int? MaxItemsPerCategory { get; set; }

        [Range(0, 600, ErrorMessage = "זמן החסד ל-No-Show חייב להיות בין 0 ל-600 דקות.")]
        public int? AutoNoShowGraceMinutes { get; set; }

        [Range(0, 4, ErrorMessage = "הקנס חייב להיות בין 0 ל-4.")]
        public double? NoShowReliabilityPenalty { get; set; }

        [Range(5, 150, ErrorMessage = "המהירות הממוצעת חייבת להיות בין 5 ל-150 קמ\"ש.")]
        public double? AverageSpeedKmH { get; set; }
    }
}
