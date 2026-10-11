using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodRescue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")] // מומלץ לוודא שרק מנהלים יכולים לגשת ולשנות הגדרות
    public class SettingsController : ControllerBase
    {
        // לצורך הדגמה או שמירה במסד נתונים / קובץ הגדרות
        private static System.Collections.Generic.Dictionary<string, object> _settings = new()
        {
            { "matchingTimeoutMinutes", "" },
            { "distanceWeight", "" },
            { "urgencyWeight",""}
        };

        [HttpGet]
        public IActionResult GetSettings()
        {
            return Ok(_settings);
        }

        [HttpPut]
        public IActionResult UpdateSettings([FromBody] System.Collections.Generic.Dictionary<string, object> newSettings)
        {
            if (newSettings == null)
            {
                return BadRequest("נתונים שגויים.");
            }

            foreach (var kvp in newSettings)
            {
                _settings[kvp.Key] = kvp.Value;
            }

            return Ok(new MessageResponse { Message = "הגדרות עודכנו בהצלחה" });
        }
    }

    public class MessageResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}