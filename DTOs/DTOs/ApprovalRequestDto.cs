using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת בקשה ע"י הלקוח (ללא שדות רגישים שהקליינט יכול לנסות לזייף)
    public class CreateApprovalRequestDto
    {
        [Required(ErrorMessage = "יש לציין את התפקיד המבוקש.")]
        public RequestedRole RequestedRole { get; set; }

        [StringLength(15)]
        public string? IdNumber { get; set; }

        [StringLength(20)]
        public string? LicenseNumber { get; set; }
    }

    // DTO מלא להצגה בשרת (למנהלים / למשתמש עצמו)
    public class ApprovalRequestResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public RequestedRole RequestedRole { get; set; }
        public ApprovalStatus Status { get; set; }
        public bool PassedAutomaticValidation { get; set; }
        public string? ValidationDetails { get; set; }
        public int? HandledByManagerId { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? HandledAt { get; set; }
    }

    // דחיית בקשה / פרופיל. הלקוח יכול לשלוח גם מחרוזת JSON פשוטה (תאימות לאחור).
    public class RejectRequestDto
    {
        [StringLength(500, ErrorMessage = "סיבת הדחייה יכולה להכיל עד 500 תווים.")]
        public string? Reason { get; set; }
    }
}
