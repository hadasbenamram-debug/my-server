using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/admin/ngos")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminNgosController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;

        public AdminNgosController(FoodRescueDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // שליפת רשימת העמותות והארגונים
        [HttpGet]
        public async Task<IActionResult> GetNgos()
        {
            var ngos = await _context.Ngos
                .Include(n => n.User)
                .Include(n => n.Region)
                .Select(n => new
                {
                    id = n.Id,
                    name = n.NgoName, // שימוש בשדה הנכון מתוך מודל Ngo
                    phone = n.User != null ? n.User.Phone : "לא צויין",
                    city = n.Address, // ניתן להשתמש בכתובת או בנתוני האזור לפי הצורך
                    status = n.ApprovalStatus.ToString()
                })
                .ToListAsync();

            return Ok(ngos);
        }
    }
}