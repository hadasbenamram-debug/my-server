using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // שידוך (תחנת איסוף אחת במסלול) כפי שהוא חוזר ללקוח
    public class MatchResponseDto
    {
        public int Id { get; set; }
        public int DonationId { get; set; }
        public int VolunteerId { get; set; }
        public int NgoId { get; set; }
        public MatchStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int? VolunteerRouteId { get; set; }
        public bool IsActive { get; set; }
        public double MatchScore { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public DateTime? ResponseDeadline { get; set; }
        public DateTime? PickedUpAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public double? GoogleMapsDistanceKm { get; set; }
        public double? GoogleMapsDurationMinutes { get; set; }

        public string? VolunteerName { get; set; }

        // נקודת האיסוף (הספק)
        public string? SupplierName { get; set; }
        public string? PickupAddress { get; set; }
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }

        // היעד (העמותה)
        public string? NgoName { get; set; }
        public string? NgoAddress { get; set; }
        public double NgoLatitude { get; set; }
        public double NgoLongitude { get; set; }
    }

    // תחנת איסוף במסלול
    public class RouteStopDto
    {
        public int Order { get; set; }
        public int MatchId { get; set; }
        public int DonationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SupplierName { get; set; }
        public string? Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public TimeSpan LatestPickupTime { get; set; }
        public double WeightKg { get; set; }
        public List<DonationCategoryLineDto> Items { get; set; } = new();
    }

    // מסלול שלם של מתנדב: כמה תורמים -> עמותה אחת
    public class RouteDto
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int VolunteerId { get; set; }
        public string? VolunteerName { get; set; }
        public DateTime ScheduledAt { get; set; }
        public DateTime? ResponseDeadline { get; set; }
        public bool AwaitingResponse { get; set; }
        public double TotalWeightKg { get; set; }
        public double? EstimatedTotalDurationMinutes { get; set; }
        public double? EstimatedTotalDistanceKm { get; set; }
        public double MatchScore { get; set; }
        public string? Polyline { get; set; }
        public int NgoId { get; set; }
        public string? NgoName { get; set; }
        public string? NgoAddress { get; set; }
        public double NgoLatitude { get; set; }
        public double NgoLongitude { get; set; }
        public DateTime? EstimatedNgoArrival { get; set; }
        public double? VolunteerLatitude { get; set; }
        public double? VolunteerLongitude { get; set; }
        public List<RouteStopDto> Stops { get; set; } = new();
    }

    // תשובה לבקשת התנדבות מתוך הקישור שבמייל (בלי התחברות). הטוקן מוגבל למסלול אחד ולפעולה אחת.
    public class RespondByLinkDto
    {
        [Required(ErrorMessage = "חסר טוקן.")]
        [StringLength(200)]
        public string Token { get; set; } = string.Empty;

        public bool Accept { get; set; }
    }

    // הפעלת No-Show ע"י מנהל
    public class NoShowDto
    {
        [StringLength(300)]
        public string? Reason { get; set; }
    }
}
