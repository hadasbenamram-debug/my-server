using BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/map")]
    [ApiController]
    [Authorize]
    public class MapController : ControllerBase
    {
        private readonly IMapService _mapService;

        public MapController(IMapService mapService)
        {
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
        }

        // שליפת כלל הישויות (ספקים, עמותות ומתנדבים) עבור תמונת המצב המרחבית - מנהלים בלבד.
        // כתובות וקואורדינטות של כל הישויות הן מידע רגיש ולכן לא נחשפות לכל משתמש מחובר.
        [HttpGet("all-entities")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllEntities()
        {
            return Ok(await _mapService.GetAllEntitiesAsync());
        }
    }
}
