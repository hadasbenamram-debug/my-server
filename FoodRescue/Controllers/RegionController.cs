using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // אזורי פעילות. אוחד עם api/lookups/regions (שהוסר) - נתיב אחד לכל משאב.
    [Authorize]
    [Route("api/[controller]")]
    public class RegionController : ApiControllerBase
    {
        private readonly IRegionService _regionService;

        public RegionController(IRegionService regionService)
        {
            _regionService = regionService;
        }

        // פתוח למשתמשים לא מחוברים - טפסי ההרשמה טוענים את האזורים
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<RegionResponseDto>>> GetAllRegions() =>
            Ok(await _regionService.GetAllAsync());

        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<RegionResponseDto>> GetRegionById(int id) =>
            Ok(await _regionService.GetByIdAsync(id) ?? throw new NotFoundException("האזור המבוקש לא נמצא."));

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<RegionResponseDto>> AddRegion([FromBody] CreateRegionDto dto) =>
            Ok(await _regionService.AddAsync(dto));

        // מחיקה - מנהלים בלבד. אזור שבשימוש (עמותה / תרומה / משבצת זמינות) מחזיר 409.
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteRegion(int id)
        {
            await _regionService.DeleteAsync(id);
            return NoContent();
        }
    }
}
