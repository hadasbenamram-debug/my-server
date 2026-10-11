using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class NgoRepository : Repository<Ngo>, INgoRepository
    {
        public NgoRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<Ngo> GetByUserIdAsync(int userId)
        {
            return await _dbSet.FirstOrDefaultAsync(n => n.UserId == userId);
        }
    }
}