using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת בקשה ע"י הלקוח (ללא שדות רגישים שהקליינט יכול לנסות לזייף)
    public class CreateApprovalRequestDto
    {
        [Required(ErrorMessage = "יש לציין את התפקיד המבוקש.")]
        public RequestedRole RequestedRole { get; set; }
        public string IdNumber { get; set; }
        public string LicenseNumber { get; set; }
    }

    // DTO מלא להצגה בשרת (למנהלים / למשתמש עצמו)
    public class ApprovalRequestResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public RequestedRole RequestedRole { get; set; }
        public ApprovalStatus Status { get; set; }
        public bool PassedAutomaticValidation { get; set; }
        public string ValidationDetails { get; set; }
        public int? HandledByManagerId { get; set; }
        public string RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? HandledAt { get; set; }
    }
}