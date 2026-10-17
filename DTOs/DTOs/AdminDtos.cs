using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // שורה ברשימת העמותות למנהל (GET api/admin/ngos)
    public class AdminNgoListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string City { get; set; }
        public string Status { get; set; }
    }

    // שורה ברשימת הספקים למנהל (GET api/admin/suppliers)
    public class AdminSupplierListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Status { get; set; }
    }

    // שורה ברשימת המתנדבים למנהל (GET api/Volunteers)
    public class AdminVolunteerListItemDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string VehicleType { get; set; }
        public double MaxCapacityKg { get; set; }
        public bool IsAvailable { get; set; }
        public string IdNumber { get; set; }
        public string DriverLicenseNumber { get; set; }
        public DateTime DateOfBirth { get; set; }
        public DateTime? DriverLicenseExpiry { get; set; }
        public double ReliabilityScore { get; set; }
        public string Status { get; set; }
    }

    // דוח מערכת (GET api/admin/volunteers/reports)
    public class SystemReportDto
    {
        public int TotalDonationsVolume { get; set; }
        public string AverageResponseTime { get; set; }
        public string SuccessRate { get; set; }
        public List<TopVolunteerDto> TopVolunteers { get; set; } = new List<TopVolunteerDto>();
    }

    public class TopVolunteerDto
    {
        public string Name { get; set; }
        public int DeliveriesCount { get; set; }
    }

    // נתוני הדשבורד (GET api/admin/dashboard-stats)
    public class DashboardStatsDto
    {
        public int OpenDonations { get; set; }
        public int AvailableVolunteers { get; set; }
        public int ActiveAssignments { get; set; }
        public int NoShowAlerts { get; set; }
    }

    // זמינות מתנדבים למנהל (GET api/admin/volunteers/availability)
    public class VolunteerAvailabilitySummaryDto
    {
        public int VolunteerId { get; set; }
        public string VolunteerName { get; set; }
        public string Day { get; set; }
        public string TimeSlot { get; set; }
    }

    // משימה פעילה במוניטור (GET api/admin/live-monitor) - מסלול פעיל של מתנדב
    public class LiveMissionDto
    {
        public int Id { get; set; }               // מזהה המסלול
        public int VolunteerId { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StopsCount { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime? LocationUpdatedAt { get; set; }
    }

    // בקשת הצטרפות ממתינה (GET api/admin/volunteers/pending-requests).
    // בעבר הוחזרה ה-Entity עצמה, כולל המשתמש עם PasswordHash. עכשיו רק השדות הנחוצים.
    public class PendingApprovalRequestDto
    {
        public int Id { get; set; }

        // מזהה הפרופיל (מתנדב / ספק / עמותה) - לפעולות /{id}/approve של כל ישות
        public int? ProfileId { get; set; }
        public string? ProfileName { get; set; }
        public string? Address { get; set; }
        public string? VehicleType { get; set; }
        public double? MaxCapacityKg { get; set; }
        public DateTime? DriverLicenseExpiry { get; set; }
        public string? TaxId { get; set; }
        public string? RegistrationNumber { get; set; }
        public bool HasLicenseFile { get; set; }

        public int UserId { get; set; }
        public UserSummaryDto User { get; set; }
        public RequestedRole RequestedRole { get; set; }
        public ApprovalStatus Status { get; set; }
        public bool PassedAutomaticValidation { get; set; }
        public string ValidationDetails { get; set; }
        public int? HandledByManagerId { get; set; }
        public string RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? HandledAt { get; set; }
    }

    public class UserSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }
}
