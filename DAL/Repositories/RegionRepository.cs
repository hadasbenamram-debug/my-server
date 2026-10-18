using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class RegionRepository : Repository<Region>, IRegionRepository
    {
        public RegionRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<bool> ExistsAsync(int id)
        {
            return await _context.Regions.AnyAsync(r => r.Id == id);
        }

        public async Task<bool> TryDeleteAsync(Region region)
        {
            _context.Regions.Remove(region);
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }
    }
}
