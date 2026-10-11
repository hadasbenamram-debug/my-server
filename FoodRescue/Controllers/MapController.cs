using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/map")]
    [ApiController]
    [Authorize]
    public class MapController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;

        public MapController(FoodRescueDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // שליפת כלל הישויות (ספקים, עמותות ומתנדבים) עבור תמונת המצב המרחבית
        [HttpGet("all-entities")]
        public async Task<IActionResult> GetAllEntities()
        {
            var entities = new List<object>();

            // שליפת ספקים/תורמים
            var suppliers = await _context.Suppliers
                .Select(s => new
                {
                    id = s.Id,
                    name = s.BusinessName,
                    latitude = s.Latitude,
                    longitude = s.Longitude,
                    address = s.Address,
                    type = "supplier"
                })
                .ToListAsync();

            // שליפת עמותות
            var ngos = await _context.Ngos
                .Select(n => new
                {
                    id = n.Id,
                    name = n.NgoName,
                    latitude = n.Latitude,
                    longitude = n.Longitude,
                    address = n.Address,
                    type = "ngo"
                })
                .ToListAsync();

            // שליפת מתנדבים (בהתאם למבנה הטבלה שלך)
            var volunteers = await _context.Volunteers
                .Include(v => v.User)
                .Select(v => new
                {
                    id = v.Id,
                    name = v.User != null ? v.User.Name : "מתנדב",
                    latitude = 0.0, // יש לעדכן אם קיימות קואורדינטות למתנדב במסד
                    longitude = 0.0,
                    address = "",
                    type = "volunteer"
                })
                .ToListAsync();

            entities.AddRange(suppliers);
            entities.AddRange(ngos);
            entities.AddRange(volunteers);

            return Ok(entities);
        }
    }
}