using DAL.Classes;
using DAL.Interfaces;

namespace DAL.Repositories
{
    public class FoodCategoryRepository : Repository<FoodCategory>, IFoodCategoryRepository
    {
        public FoodCategoryRepository(FoodRescueDbContext context) : base(context) { }
    }
}
