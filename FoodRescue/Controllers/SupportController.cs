using BLL.Interfaces;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Route("api/[controller]")]
    public class SupportController : ApiControllerBase
    {
        private readonly ISupportService _supportService;

        public SupportController(ISupportService supportService)
        {
            _supportService = supportService;
        }

        // POST api/support/contact - פתוח גם לאורחים, ולכן מוגבל בקצב.
        // תוקן: פנייה מאורח נשמרת בלי משתמש (null) ולא על משתמש 1.
        [HttpPost("contact")]
        [AllowAnonymous]
        [EnableRateLimiting("contact-form")]
        public async Task<IActionResult> Contact([FromBody] ContactMessageDto dto)
        {
            await _supportService.SendContactMessageAsync(dto, User?.GetUserId());
            return Message("הפנייה התקבלה ותישלח לצוות בדקות הקרובות.");
        }
    }
}
