using BLL.Exceptions;
using BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // מפות. המפתח של Google נמצא רק בשרת: השרת מביא מ-Google את תמונת המפה ומחזיר אותה ללקוח.
    [Route("api/map")]
    [Authorize]
    public class MapController : ApiControllerBase
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

        // תמונת המפה של כל הישויות - מנהלים בלבד. ?hide=volunteer,ngo - סוגים שלא יוצגו
        [HttpGet("all-entities/image")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllEntitiesImage([FromQuery] string? hide)
        {
            var hidden = (hide ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var image = await _mapService.GetAllEntitiesImageAsync(hidden);
            return File(image.Content, image.ContentType);
        }

        // תמונת המפה של מסלול: מנהל, המתנדב, העמותה, או ספק שתרומה שלו במסלול. אחרת 404.
        [HttpGet("routes/{routeId:int}/image")]
        public async Task<IActionResult> GetRouteImage(int routeId)
        {
            var image = await _mapService.GetRouteImageAsync(routeId, CurrentUser)
                        ?? throw new NotFoundException("המסלול לא נמצא.");
            return File(image.Content, image.ContentType);
        }
    }
}
