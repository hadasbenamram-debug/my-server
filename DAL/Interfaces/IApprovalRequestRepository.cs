using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IApprovalRequestRepository : IRepository<ApprovalRequest>
    {
        // בקשת האישור העדכנית ביותר של משתמש לתפקיד מסוים
        Task<ApprovalRequest?> GetLatestByUserAndRoleAsync(int userId, RequestedRole role);

        // בקשות ממתינות (כולל פרטי המשתמש). role == null = כל התפקידים.
        Task<List<ApprovalRequest>> GetPendingWithUserAsync(RequestedRole? role);

        Task<PagedResult<ApprovalRequest>> GetPageAsync(int skip, int take);
    }
}
