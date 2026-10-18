using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // קטגוריות מזון. אוחד עם api/lookups/categories (שהוסר) - נתיב אחד לכל משאב.
    [Authorize]
    [Route("api/[controller]")]
    public class CategoriesController : ApiControllerBase
    {
        private readonly IFoodCategoryService _categoryService;

        public CategoriesController(IFoodCategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        // תוקן: מחזיר FoodCategoryResponseDto כולל Id (בעבר ב-lookups הוחזר DTO בלי Id)
        [HttpGet]
        public async Task<ActionResult<List<FoodCategoryResponseDto>>> GetCategories() =>
            Ok(await _categoryService.GetAllAsync());

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<FoodCategoryResponseDto>> AddCategory([FromBody] CreateFoodCategoryDto dto) =>
            Ok(await _categoryService.AddAsync(dto));
    }
}
