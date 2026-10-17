using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class AvailabilitySlotRepository : Repository<AvailabilitySlot>, IAvailabilitySlotRepository
    {
        public AvailabilitySlotRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<AvailabilitySlot>> GetByVolunteerIdWithRegionsAsync(int volunteerId)
        {
            return await _context.AvailabilitySlots
                .Include(s => s.Region)
                .Include(s => s.ToRegion)
                .Where(s => s.VolunteerId == volunteerId)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task ReplaceForVolunteerAsync(int volunteerId, IEnumerable<AvailabilitySlot> newSlots)
        {
            var existingSlots = await _context.AvailabilitySlots
                .Where(s => s.VolunteerId == volunteerId)
                .ToListAsync();

            _context.AvailabilitySlots.RemoveRange(existingSlots);
            _context.AvailabilitySlots.AddRange(newSlots);

            await _context.SaveChangesAsync();
        }

        public async Task<bool> AnyInRegionAsync(int regionId)
        {
            return await _context.AvailabilitySlots.AnyAsync(a => a.RegionId == regionId || a.ToRegionId == regionId);
        }
    }
}
