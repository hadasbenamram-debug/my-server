using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/admin/suppliers")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminSuppliersController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;

        public AdminSuppliersController(FoodRescueDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // שליפת רשימת הספקים והתורמים
        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            var suppliers = await _context.Suppliers
                .Include(s => s.User) // טעינת נתוני המשתמש המקושר עבור פרטי קשר
                .Select(s => new
                {
                    id = s.Id,
                    name = s.BusinessName, // שימוש בשדה הנכון מתוך מודל Supplier
                    phone = s.User != null ? s.User.Phone : "לא צויין", // שליפת טלפון מטבלת User אם קיים
                    address = s.Address,
                    status = s.ApprovalStatus.ToString()
                })
                .ToListAsync();

            return Ok(suppliers);
        }
    }
}