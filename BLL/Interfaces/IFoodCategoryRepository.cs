using DAL.Classes;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IFoodCategoryRepository : IRepository<FoodCategory>
    {
        Task<FoodCategory> GetByNameAsync(string name);
    }
}