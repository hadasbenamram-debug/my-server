using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class ManagerRepository : Repository<Manager>, IManagerRepository
    {
        public ManagerRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<Manager?> GetByUserIdAsync(int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(m => m.UserId == userId);
        }
    }
}
