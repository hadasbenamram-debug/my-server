using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class MatchRepository : Repository<Match>, IMatchRepository
    {
        public MatchRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<IEnumerable<Match>> GetActiveMatchesAsync()
        {
            return await _context.Matches
                .Include(m => m.Donation)
                .Include(m => m.Ngo)
                .Include(m => m.Volunteer)
                .Where(m => m.Status == MatchStatus.Confirmed || m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                .ToListAsync();
        }

        public async Task<bool> HasActiveConfirmedMatchAsync(int volunteerId, int excludeMatchId, int? excludeRouteId = null)
        {
            return await _context.Matches.AnyAsync(m =>
                m.VolunteerId == volunteerId &&
                m.Id != excludeMatchId &&
                (excludeRouteId == null || m.VolunteerRouteId != excludeRouteId) &&
                (m.Status == MatchStatus.Confirmed ||
                 m.Status == MatchStatus.InPickup ||
                 m.Status == MatchStatus.InTransit));
        }

        public async Task<IEnumerable<Match>> GetMatchesForNgoTodayAsync(int ngoId)
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            return await _context.Matches
                .Include(m => m.Donation)
                .Where(m => m.NgoId == ngoId && m.AssignedAt >= today && m.AssignedAt < tomorrow && m.Status == MatchStatus.Confirmed)
                .ToListAsync();
        }
    }
}