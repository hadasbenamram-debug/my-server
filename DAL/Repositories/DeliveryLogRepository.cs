using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
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

        public async Task<List<DeliveryLog>> GetForRouteAsync(int routeId, int maxRows)
        {
            // הרישומים האחרונים (האינדקס VolunteerRouteId + Timestamp), ואז בחזרה לסדר כרונולוגי
            var latest = await _context.DeliveryLogs
                .Where(l => l.VolunteerRouteId == routeId)
                .OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id)
                .Take(maxRows)
                .AsNoTracking()
                .ToListAsync();

            latest.Reverse();
            return latest;
        }

        public async Task<DeliveryLog?> GetLastLocationAsync(int routeId)
        {
            return await _context.DeliveryLogs
                .Where(l => l.VolunteerRouteId == routeId && l.Type == DeliveryLogType.Location)
                .OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        public async Task<bool> TryAddAsync(DeliveryLog log)
        {
            try
            {
                await AddAsync(log);
                return true;
            }
            catch (DbUpdateException)
            {
                // שלא יישמר "בטעות" ב-SaveChanges הבא של אותה בקשה
                _context.Entry(log).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<int> DeleteLocationsOlderThanAsync(DateTime cutoffUtc)
        {
            var old = _context.DeliveryLogs.Where(l => l.Type == DeliveryLogType.Location && l.Timestamp < cutoffUtc);

            // ב-SQL Server: מחיקה אחת במסד, בלי לטעון את השורות לזיכרון. במסד בזיכרון (בדיקות) אין ExecuteDelete.
            if (_context.Database.IsRelational())
                return await old.ExecuteDeleteAsync();

            var rows = await old.ToListAsync();
            _context.DeliveryLogs.RemoveRange(rows);
            await _context.SaveChangesAsync();
            return rows.Count;
        }
    }
}
