using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;

        public CategoriesController(FoodRescueDbContext context)
        {
            _context = context ?? throw new System.ArgumentNullException(nameof(context));
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            // שימוש בשם הנכון של הטבלה במסד הנתונים: FoodCategories
            var categories = await _context.FoodCategories
                .Select(c => new
                {
                    id = c.Id,
                    name = c.CategoryName // שימוש בשדה הנכון מתוך מחלקת FoodCategory
                })
                .ToListAsync();

            return Ok(categories);
        }
    }
}