using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class FeedbackRepository : Repository<Feedback>, IFeedbackRepository
    {
        public FeedbackRepository(FoodRescueDbContext context) : base(context) { }

        private IQueryable<Feedback> WithVolunteer() => _context.Feedbacks
            .Include(f => f.Match).ThenInclude(m => m.Volunteer).ThenInclude(v => v.User);

        public async Task<List<Feedback>> GetForVolunteerAsync(int volunteerId)
        {
            return await WithVolunteer()
                .Where(f => f.Match.VolunteerId == volunteerId)
                .OrderByDescending(f => f.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Feedback>> GetForNgoOrSupplierAsync(int ngoId, int supplierId)
        {
            return await WithVolunteer()
                .Where(f => f.Match.NgoId == ngoId || f.Match.Donation.SupplierId == supplierId)
                .OrderByDescending(f => f.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<PagedResult<Feedback>> GetPageAsync(int skip, int take)
        {
            var query = WithVolunteer().OrderByDescending(f => f.CreatedAt).AsNoTracking();
            return PageAsync(query, skip, take);
        }

        public async Task<bool> ExistsForMatchAsync(int matchId)
        {
            return await _context.Feedbacks.AnyAsync(f => f.MatchId == matchId);
        }

        public async Task<List<int>> GetVolunteerRatingsAsync(int volunteerId)
        {
            return await _context.Feedbacks
                .Where(f => f.Match.VolunteerId == volunteerId && f.VolunteerRating >= 1)
                .Select(f => f.VolunteerRating)
                .ToListAsync();
        }
    }
}
