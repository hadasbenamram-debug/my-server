using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class VolunteerRouteRepository : Repository<VolunteerRoute>, IVolunteerRouteRepository
    {
        public VolunteerRouteRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<VolunteerRoute?> GetWithDetailsAsync(int routeId)
        {
            return await _context.VolunteerRoutes
                .Include(r => r.Volunteer).ThenInclude(v => v.User)
                .Include(r => r.Matches).ThenInclude(m => m.Donation).ThenInclude(d => d.Supplier)
                .Include(r => r.Matches).ThenInclude(m => m.Donation).ThenInclude(d => d.DonationFoodCategories).ThenInclude(c => c.Category)
                .Include(r => r.Matches).ThenInclude(m => m.Ngo)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == routeId);
        }

        public async Task<VolunteerRoute?> GetActiveForVolunteerAsync(int volunteerId)
        {
            var routeId = await _context.VolunteerRoutes
                .Where(r => r.VolunteerId == volunteerId &&
                            (r.Status == RouteStatus.Planned || r.Status == RouteStatus.InProgress) &&
                            r.Matches.Any(m => m.IsActive))
                .OrderByDescending(r => r.ScheduledAt)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();

            return routeId.HasValue ? await GetWithDetailsAsync(routeId.Value) : null;
        }

        public async Task<int?> GetInProgressRouteIdAsync(int volunteerId)
        {
            return await _context.VolunteerRoutes
                .Where(r => r.VolunteerId == volunteerId &&
                            r.Status == RouteStatus.InProgress &&
                            r.Matches.Any(m => m.IsActive))
                .OrderByDescending(r => r.StartedAt)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<VolunteerRoute>> GetInProgressWithDetailsAsync()
        {
            return await _context.VolunteerRoutes
                .Where(r => r.Status == RouteStatus.InProgress && r.Matches.Any(m => m.IsActive))
                .Include(r => r.Volunteer).ThenInclude(v => v.User)
                .Include(r => r.Matches).ThenInclude(m => m.Donation).ThenInclude(d => d.Supplier)
                .Include(r => r.Matches).ThenInclude(m => m.Donation).ThenInclude(d => d.DonationFoodCategories).ThenInclude(c => c.Category)
                .Include(r => r.Matches).ThenInclude(m => m.Ngo)
                .OrderBy(r => r.StartedAt)
                .AsSplitQuery()
                .ToListAsync();
        }
    }
}
