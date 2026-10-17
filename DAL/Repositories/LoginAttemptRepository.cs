using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class LoginAttemptRepository : Repository<LoginAttempt>, ILoginAttemptRepository
    {
        public LoginAttemptRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<DateTime>> GetRecentFailuresAsync(string email, string? ipAddress, DateTime sinceUtc)
        {
            // התחברות מוצלחת מאותו IP מאפסת את הספירה
            var lastSuccess = await _context.LoginAttempts
                .Where(l => l.Email == email && l.IpAddress == ipAddress && l.WasSuccessful && l.AttemptedAt >= sinceUtc)
                .MaxAsync(l => (DateTime?)l.AttemptedAt);

            var from = lastSuccess.HasValue && lastSuccess.Value > sinceUtc ? lastSuccess.Value : sinceUtc;

            return await _context.LoginAttempts
                .Where(l => l.Email == email && l.IpAddress == ipAddress && !l.WasSuccessful && l.AttemptedAt > from)
                .OrderByDescending(l => l.AttemptedAt)
                .Select(l => l.AttemptedAt)
                .ToListAsync();
        }

        public async Task<List<DateTime>> GetRecentFailuresByIpAsync(string ipAddress, DateTime sinceUtc, int take)
        {
            return await _context.LoginAttempts
                .Where(l => l.IpAddress == ipAddress && !l.WasSuccessful && l.AttemptedAt >= sinceUtc)
                .OrderByDescending(l => l.AttemptedAt)
                .Select(l => l.AttemptedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
        {
            var old = await _context.LoginAttempts.Where(l => l.AttemptedAt < cutoffUtc).ToListAsync();
            _context.LoginAttempts.RemoveRange(old);
            await _context.SaveChangesAsync();
            return old.Count;
        }
    }
}
