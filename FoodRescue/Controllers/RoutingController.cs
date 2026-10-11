using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace API.Controllers
{
    [Route("api/routing")]
    [ApiController]
    [Authorize]
    public class RoutingController : ControllerBase
    {
        // שליפת תוכנית המסלול הנוכחית
        [HttpGet("current-plan")]
        public IActionResult GetCurrentPlan()
        {
            // ניתן להחזיר רשימה ריקה כבסיס לפיתוח, או לשלוף את התחנות ממסד הנתונים
            var currentPlan = new List<object>();

            return Ok(currentPlan);
        }
    }
}