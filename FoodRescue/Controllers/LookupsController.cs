using AutoMapper;
using DAL;
using DAL.Classes;
using DTOs.DTOs;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LookupsController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;
        private readonly IMapper _mapper;

        public LookupsController(FoodRescueDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        // שליפת קטגוריות - פתוח לכולם (ללא צורך באימות)
        [HttpGet("categories")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<CreateFoodCategoryDto>>> GetCategories()
        {
            var categories = await _context.FoodCategories.ToListAsync();
            var categoryDtos = _mapper.Map<IEnumerable<CreateFoodCategoryDto>>(categories);
            return Ok(categoryDtos);
        }

        // הוספת קטגוריה - מוגבל למנהלים בלבד (סעיף 11)
        [HttpPost("categories")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<CreateFoodCategoryDto>> AddCategory([FromBody] CreateFoodCategoryDto categoryDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var categoryEntity = _mapper.Map<FoodCategory>(categoryDto);

            // אבטחה: איפוס מזהה כדי למנוע דריסת ID או הזרקת ערכים מצד הלקוח
            categoryEntity.Id = 0;

            _context.FoodCategories.Add(categoryEntity);
            await _context.SaveChangesAsync();

            var resultDto = _mapper.Map<CreateFoodCategoryDto>(categoryEntity);
            return Ok(resultDto);
        }

        // שליפת אזורים - פתוח לכולם (ללא צורך באימות)
        [HttpGet("regions")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<CreateRegionDto>>> GetRegions()
        {
            var regions = await _context.Regions.ToListAsync();
            var regionDtos = _mapper.Map<IEnumerable<CreateRegionDto>>(regions);
            return Ok(regionDtos);
        }

        // הוספת אזור - מוגבל למנהלים בלבד (סעיף 11)
        [HttpPost("regions")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<CreateRegionDto>> AddRegion([FromBody] CreateRegionDto regionDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var regionEntity = _mapper.Map<Region>(regionDto);

            // אבטחה: איפוס מזהה כדי למנוע דריסת ID מצד הלקוח
            regionEntity.Id = 0;

            _context.Regions.Add(regionEntity);
            await _context.SaveChangesAsync();

            var resultDto = _mapper.Map<CreateRegionDto>(regionEntity);
            return Ok(resultDto);
        }
    }
}