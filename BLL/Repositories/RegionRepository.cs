using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class RegionRepository : Repository<Region>, IRegionRepository
    {
        public RegionRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<IEnumerable<Region>> GetAllRegionsAsync()
        {
            return await _context.Regions
                .Include(r => r.Donations)
                .ToListAsync();
        }
    }
}