using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IFoodCategoryService
    {
        // כל הקטגוריות כולל מזהה (לבחירה ברשימה)
        Task<List<FoodCategoryResponseDto>> GetAllAsync();
        Task<FoodCategoryResponseDto> AddAsync(CreateFoodCategoryDto dto);
    }
}
