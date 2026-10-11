using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class ApprovalRequestRepository : Repository<ApprovalRequest>, IApprovalRequestRepository
    {
        public ApprovalRequestRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<IEnumerable<ApprovalRequest>> GetPendingRequestsAsync()
        {
            return await _context.ApprovalRequests
                .Include(ar => ar.User)
                .Where(ar => ar.Status == ApprovalStatus.Pending)
                .ToListAsync();
        }
    }
}