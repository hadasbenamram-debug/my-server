using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class VolunteerRepository : Repository<Volunteer>, IVolunteerRepository
    {
        public VolunteerRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<IEnumerable<Volunteer>> GetAvailableVolunteersAsync()
        {
            return await _context.Volunteers
                .Where(v => v.IsAvailable)
                .Include(v => v.AvailabilitySlots)
                .Include(v => v.User)
                .ToListAsync();
        }

        public async Task<List<Volunteer>> GetAllActiveAsync()
        {
            return await _context.Volunteers
                .Where(v => v.IsAvailable)
                .Include(v => v.AvailabilitySlots)
                .Include(v => v.User)
                .ToListAsync();
        }

        public async Task<Volunteer> GetByUserIdAsync(int userId)
        {
            return await _context.Volunteers
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.UserId == userId);
        }
    }
}