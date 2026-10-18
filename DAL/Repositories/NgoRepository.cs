using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class NgoRepository : Repository<Ngo>, INgoRepository
    {
        public NgoRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<Ngo?> GetByUserIdAsync(int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(n => n.UserId == userId);
        }

        public async Task<List<Ngo>> GetAllWithUserAsync()
        {
            return await _context.Ngos
                .Include(n => n.User)
                .Include(n => n.Region)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Ngo>> GetApprovedAsync()
        {
            return await _context.Ngos
                .Where(n => n.ApprovalStatus == ApprovalStatus.Approved)
                .ToListAsync();
        }

        public async Task<bool> AnyInRegionAsync(int regionId)
        {
            return await _context.Ngos.AnyAsync(n => n.RegionId == regionId);
        }
    }
}
