using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BLL.Repositories
{
    public class FoodCategoryRepository : Repository<FoodCategory>, IFoodCategoryRepository
    {
        public FoodCategoryRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<FoodCategory> GetByNameAsync(string name)
        {
            return await _context.FoodCategories.FirstOrDefaultAsync(c => c.CategoryName == name);
        }
    }
}