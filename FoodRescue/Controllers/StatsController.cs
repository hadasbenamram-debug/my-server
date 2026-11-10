using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // תיקון 31: נתוני "ההשפעה שלנו" בדף הבית - פתוח גם לאורחים (רק מספרים מצטברים, בלי פרטים אישיים)
    [Route("api/[controller]")]
    public class StatsController : ApiControllerBase
    {
        private readonly IPublicStatsService _stats;

        public StatsController(IPublicStatsService stats)
        {
            _stats = stats;
        }

        // GET api/stats/public. השרת שומר את התוצאה במטמון ל-5 דקות, וגם הדפדפן רשאי לשמור אותה ל-5 דקות.
        [HttpGet("public")]
        [AllowAnonymous]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<PublicStatsDto>> GetPublicStats() =>
            Ok(await _stats.GetAsync());
    }
}
