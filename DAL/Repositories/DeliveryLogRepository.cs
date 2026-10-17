using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class DeliveryLogRepository : Repository<DeliveryLog>, IDeliveryLogRepository
    {
        public DeliveryLogRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<DeliveryLog>> GetByVolunteerIdAsync(int volunteerId)
        {
            return await _context.DeliveryLogs
                .Where(l => l.VolunteerRoute.VolunteerId == volunteerId)
                .OrderByDescending(l => l.Timestamp)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<PagedResult<DeliveryLog>> GetPageAsync(int skip, int take)
        {
            var query = _context.DeliveryLogs.OrderByDescending(l => l.Timestamp).AsNoTracking();
            return PageAsync(query, skip, take);
        }
    }
}
