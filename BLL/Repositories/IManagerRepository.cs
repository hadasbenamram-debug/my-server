using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class ManagerRepository : Repository<Manager>, IManagerRepository
    {
        public ManagerRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<Manager> GetByUserIdAsync(int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(m => m.UserId == userId);
        }
    }
}