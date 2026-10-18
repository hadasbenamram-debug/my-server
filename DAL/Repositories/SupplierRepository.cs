using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class SupplierRepository : Repository<Supplier>, ISupplierRepository
    {
        public SupplierRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<Supplier?> GetByUserIdAsync(int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(s => s.UserId == userId);
        }

        public async Task<Supplier?> GetOwnedAsync(int id, int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        }

        public async Task<List<Supplier>> GetAllWithUserAsync()
        {
            return await _context.Suppliers
                .Include(s => s.User)
                .AsNoTracking()
                .ToListAsync();
        }
    }
}
