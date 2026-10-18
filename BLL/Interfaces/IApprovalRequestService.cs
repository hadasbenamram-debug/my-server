using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // מנגנון האישור היחיד במערכת. גם ApprovalRequestsController וגם /{id}/approve של כל ישות
    // עוברים כאן, כך שבכל המסלולים: נשלח מייל, נשמר HandledByManagerId, בקשת האישור מתעדכנת
    // (גם בדחייה), והתפקיד (Role) של המשתמש נקבע באותו אופן.
    // הממשק עובד רק עם DTOs (כולל ה-enum RequestedRole של DTOs), כדי שהקונטרולרים לא יכירו טיפוסים של DAL.
    public interface IApprovalRequestService
    {
        Task<PagedResponse<ApprovalRequestResponseDto>> GetPageAsync(PageRequest page);
        Task<ApprovalRequestResponseDto?> GetByIdAsync(int id);
        Task<List<PendingApprovalRequestDto>> GetPendingAsync(RequestedRole? role);

        // managerUserId = המשתמש המחובר (מה-JWT)
        Task ApproveRequestAsync(int requestId, int? managerUserId);
        Task RejectRequestAsync(int requestId, int? managerUserId, string? rejectionReason);

        // לפי מזהה הפרופיל (מזהה המתנדב / הספק / העמותה - לא מזהה המשתמש)
        Task ApproveProfileAsync(RequestedRole role, int profileId, int? managerUserId);
        Task RejectProfileAsync(RequestedRole role, int profileId, int? managerUserId, string? rejectionReason);

        // בקשה שהמשתמש המחובר מגיש (עם בדיקה אוטומטית)
        Task<ApprovalRequestResponseDto> CreateAsync(CreateApprovalRequestDto dto, int userId);

        // בקשה ממתינה שנוצרת בזמן ההרשמה (בתוך הטרנזקציה של ההרשמה)
        Task AddPendingAsync(int userId, RequestedRole role, bool passedAutomaticValidation, string validationDetails);
    }
}
