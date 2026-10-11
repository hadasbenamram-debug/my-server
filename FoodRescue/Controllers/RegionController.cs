using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
[ApiController]
[Route("api/[controller]")]
public class RegionController : ControllerBase
{
    private readonly FoodRescueDbContext _context;

    public RegionController(FoodRescueDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// שליפת כל האזורים במערכת
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllRegions()
    {
        var regions = await _context.Regions.ToListAsync();
        return Ok(regions);
    }

    /// <summary>
    /// שליפת אזור ספציפי לפי מזהה
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRegionById(int id)
    {
        var region = await _context.Regions.FindAsync(id);

        if (region == null)
        {
            return NotFound(new { message = "האזור המבוקש לא נמצא." });
        }

        return Ok(region);
    }

    ///// <summary>
    ///// הוספת אזור חדש
    ///// </summary>
    //[HttpPost]
    //public async Task<IActionResult> CreateRegion([FromBody] Region region)
    //{
    //    if (region == null)
    //    {
    //        return BadRequest(new { message = "נתוני האזור שגויים." });
    //    }

    //    _context.Regions.Add(region);
    //    await _context.SaveChangesAsync();

    //    return CreatedAtAction(nameof(GetRegionById), new { id = region.Id }, region);
    //}

    /// <summary>
    /// עדכון אזור קיים
    /// </summary>
    //[HttpPut("{id}")]
    //public async Task<IActionResult> UpdateRegion(int id, [FromBody] Region updatedRegion)
    //{
    //    if (updatedRegion == null || id != updatedRegion.Id)
    //    {
    //        return BadRequest(new { message = "מזהה האזור אינו תואם." });
    //    }

    //    var region = await _context.Regions.FindAsync(id);
    //    if (region == null)
    //    {
    //        return NotFound(new { message = "האזור לעדכון לא נמצא." });
    //    }

    //    // עדכון השדות הרלוונטיים (בהתאם למבנה המחלקה במסד הנתונים)
    //    region.Name = updatedRegion.Name;

    //    await _context.SaveChangesAsync();

    //    return NoContent();
    //}

    /// <summary>
    /// מחיקת אזור
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRegion(int id)
    {
        var region = await _context.Regions.FindAsync(id);
        if (region == null)
        {
            return NotFound(new { message = "האזור למחיקה לא נמצא." });
        }

        _context.Regions.Remove(region);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}