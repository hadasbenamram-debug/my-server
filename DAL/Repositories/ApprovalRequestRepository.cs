using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class ApprovalRequestRepository : Repository<ApprovalRequest>, IApprovalRequestRepository
    {
        public ApprovalRequestRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<ApprovalRequest?> GetLatestByUserAndRoleAsync(int userId, RequestedRole role)
        {
            return await _context.ApprovalRequests
                .Where(ar => ar.UserId == userId && ar.RequestedRole == role)
                .OrderByDescending(ar => ar.CreatedAt)
                .ThenByDescending(ar => ar.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ApprovalRequest>> GetPendingWithUserAsync(RequestedRole? role)
        {
            var query = _context.ApprovalRequests
                .Include(ar => ar.User)
                .Where(ar => ar.Status == ApprovalStatus.Pending);

            if (role.HasValue)
                query = query.Where(ar => ar.RequestedRole == role.Value);

            return await query.OrderBy(ar => ar.CreatedAt).AsNoTracking().ToListAsync();
        }

        public Task<PagedResult<ApprovalRequest>> GetPageAsync(int skip, int take)
        {
            var query = _context.ApprovalRequests.OrderByDescending(ar => ar.CreatedAt).AsNoTracking();
            return PageAsync(query, skip, take);
        }
    }
}
