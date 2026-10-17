using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class ActionTokenRepository : Repository<ActionToken>, IActionTokenRepository
    {
        public ActionTokenRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<ActionToken?> FindActiveByHashAsync(ActionTokenPurpose purpose, string tokenHash, DateTime nowUtc)
        {
            return await _context.ActionTokens.FirstOrDefaultAsync(t =>
                t.Purpose == purpose &&
                t.TokenHash == tokenHash &&
                t.ConsumedAt == null &&
                t.ExpiresAt > nowUtc);
        }

        public async Task<ActionToken?> GetLatestForUserAsync(int userId, ActionTokenPurpose purpose)
        {
            return await _context.ActionTokens
                .Where(t => t.UserId == userId && t.Purpose == purpose)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<ActionToken?> FindActiveForUserAsync(int userId, ActionTokenPurpose purpose, string targetValue, DateTime nowUtc)
        {
            return await _context.ActionTokens.FirstOrDefaultAsync(t =>
                t.UserId == userId &&
                t.Purpose == purpose &&
                t.TargetValue == targetValue &&
                t.ConsumedAt == null &&
                t.ExpiresAt > nowUtc);
        }

        public async Task ConsumeActiveAsync(int? userId, ActionTokenPurpose purpose, int? targetId, DateTime nowUtc)
        {
            var tokens = await _context.ActionTokens
                .Where(t => t.Purpose == purpose && t.ConsumedAt == null &&
                            (userId == null || t.UserId == userId) &&
                            (targetId == null || t.TargetId == targetId))
                .ToListAsync();

            if (tokens.Count == 0) return;

            foreach (var t in tokens)
                t.ConsumedAt = nowUtc;

            await _context.SaveChangesAsync();
        }

        public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc)
        {
            var old = await _context.ActionTokens
                .Where(t => t.ExpiresAt < cutoffUtc || (t.ConsumedAt != null && t.ConsumedAt < cutoffUtc))
                .ToListAsync();
            _context.ActionTokens.RemoveRange(old);
            await _context.SaveChangesAsync();
            return old.Count;
        }
    }
}
