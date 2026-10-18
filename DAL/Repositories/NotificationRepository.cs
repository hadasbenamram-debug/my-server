using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class NotificationRepository : Repository<Notification>, INotificationRepository
    {
        public NotificationRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<bool> HasRecentNonFailedAsync(int userId, string subject, DateTime sinceUtc)
        {
            return await _context.Notifications.AnyAsync(n =>
                n.UserId == userId &&
                n.Subject == subject &&
                n.CreatedAt >= sinceUtc &&
                n.Channel != NotificationChannel.InApp &&
                n.Status != NotificationStatus.Failed);
        }

        public async Task<bool> TryClaimForSendingAsync(int id, DateTime nowUtc)
        {
            if (_context.Database.IsRelational())
            {
                // UPDATE אטומי אחד: רק מי שהצליח להעביר את השורה מ-Pending ל-Sending שולח אותה
                int rows = await _context.Notifications
                    .Where(n => n.Id == id &&
                                n.Status == NotificationStatus.Pending &&
                                (n.NextAttemptAt == null || n.NextAttemptAt <= nowUtc))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.Status, NotificationStatus.Sending)
                        .SetProperty(n => n.LastAttemptAt, nowUtc));
                return rows == 1;
            }

            var entity = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id);
            if (entity == null || entity.Status != NotificationStatus.Pending ||
                (entity.NextAttemptAt != null && entity.NextAttemptAt > nowUtc))
                return false;

            entity.Status = NotificationStatus.Sending;
            entity.LastAttemptAt = nowUtc;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Notification?> GetFreshAsync(int id)
        {
            var entity = await _context.Notifications.FindAsync(id);
            if (entity != null)
                await _context.Entry(entity).ReloadAsync();
            return entity;
        }

        public async Task<List<int>> GetDueIdsAsync(DateTime nowUtc, int take)
        {
            return await _context.Notifications
                .Where(n => n.Channel != NotificationChannel.InApp &&
                            n.Status == NotificationStatus.Pending &&
                            (n.NextAttemptAt == null || n.NextAttemptAt <= nowUtc))
                .OrderBy(n => n.CreatedAt)
                .Select(n => n.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> ResetStuckSendingAsync(DateTime olderThanUtc)
        {
            var stuck = await _context.Notifications
                .Where(n => n.Status == NotificationStatus.Sending && n.LastAttemptAt < olderThanUtc)
                .ToListAsync();

            foreach (var n in stuck)
                n.Status = NotificationStatus.Pending;

            if (stuck.Count > 0)
                await _context.SaveChangesAsync();

            return stuck.Count;
        }

        public Task<PagedResult<Notification>> GetInAppForUserAsync(int userId, int skip, int take)
        {
            var query = _context.Notifications
                .Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp)
                .OrderByDescending(n => n.CreatedAt)
                .AsNoTracking();
            return PageAsync(query, skip, take);
        }

        public async Task<int> CountUnreadInAppAsync(int userId)
        {
            return await _context.Notifications.CountAsync(n =>
                n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null);
        }

        public async Task MarkInAppReadAsync(int userId, IReadOnlyCollection<int>? ids, DateTime nowUtc)
        {
            var query = _context.Notifications.Where(n =>
                n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null);

            if (ids != null && ids.Count > 0)
                query = query.Where(n => ids.Contains(n.Id));

            var items = await query.ToListAsync();
            foreach (var n in items)
                n.ReadAt = nowUtc;

            if (items.Count > 0)
                await _context.SaveChangesAsync();
        }
    }
}
