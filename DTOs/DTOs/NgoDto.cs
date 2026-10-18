using System;

namespace FoodRescue.DTOs
{
    // Minimal view for approved volunteers: where to deliver and when. No RegistrationNumber, capacity or status.
    public class NgoPublicDto
    {
        public int Id { get; set; }
        public string NgoName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public TimeSpan SunToThuOpenTime { get; set; }
        public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
    }

    public class NgoResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string NgoName { get; set; } = string.Empty;
        public int DailyCapacity { get; set; }
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int RegionId { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public TimeSpan SunToThuOpenTime { get; set; }
        public TimeSpan SunToThuCloseTime { get; set; }
        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
        public int ApprovalStatus { get; set; }
    }
}
