using AutoMapper;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class FoodCategoryService : IFoodCategoryService
    {
        private readonly IFoodCategoryRepository _categoryRepository;
        private readonly IMapper _mapper;

        public FoodCategoryService(IFoodCategoryRepository categoryRepository, IMapper mapper)
        {
            _categoryRepository = categoryRepository;
            _mapper = mapper;
        }

        // תוקן: הרשימה הוחזרה בעבר כ-CreateFoodCategoryDto - בלי Id, ולכן אי אפשר היה לבחור קטגוריה
        public async Task<List<FoodCategoryResponseDto>> GetAllAsync()
        {
            var categories = await _categoryRepository.GetAllAsync();
            return _mapper.Map<List<FoodCategoryResponseDto>>(categories.OrderBy(c => c.CategoryName));
        }

        public async Task<FoodCategoryResponseDto> AddAsync(CreateFoodCategoryDto dto)
        {
            var categoryEntity = _mapper.Map<FoodCategory>(dto);
            categoryEntity.Id = 0;
            categoryEntity.CategoryName = dto.CategoryName.Trim();

            await _categoryRepository.AddAsync(categoryEntity);
            return _mapper.Map<FoodCategoryResponseDto>(categoryEntity);
        }
    }
}
