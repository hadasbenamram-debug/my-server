using DAL;
using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IApprovalRequestRepository : IRepository<ApprovalRequest>
    {
        Task<IEnumerable<ApprovalRequest>> GetPendingRequestsAsync();
    }
}