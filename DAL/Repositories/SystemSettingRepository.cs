using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class SystemSettingRepository : ISystemSettingRepository
    {
        private readonly FoodRescueDbContext _context;

        public SystemSettingRepository(FoodRescueDbContext context)
        {
            _context = context;
        }

        public async Task<List<SystemSetting>> GetAllAsync()
        {
            return await _context.SystemSettings.AsNoTracking().ToListAsync();
        }

        public async Task UpsertManyAsync(IReadOnlyDictionary<string, string> values, int? updatedByUserId)
        {
            var now = DateTime.UtcNow;
            foreach (var (key, value) in values)
            {
                var existing = await _context.SystemSettings.FindAsync(key);
                if (existing == null)
                {
                    _context.SystemSettings.Add(new SystemSetting
                    {
                        Key = key,
                        Value = value,
                        UpdatedAt = now,
                        UpdatedByUserId = updatedByUserId
                    });
                }
                else
                {
                    existing.Value = value;
                    existing.UpdatedAt = now;
                    existing.UpdatedByUserId = updatedByUserId;
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
