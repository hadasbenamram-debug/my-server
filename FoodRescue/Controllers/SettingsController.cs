using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // הגדרות מנוע השידוכים. תוקן: בעבר ההגדרות נשמרו במילון בזיכרון ולא השפיעו על המנוע.
    // עכשיו הן נשמרות במסד (SystemSettings) והמנוע קורא אותן מיד (IMatchingPolicyProvider).
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class SettingsController : ApiControllerBase
    {
        private readonly ISettingsService _settingsService;

        public SettingsController(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [HttpGet]
        public ActionResult<SystemSettingsDto> GetSettings() => Ok(_settingsService.GetCurrent());

        // רק השדות שנשלחו (לא null) מתעדכנים
        [HttpPut]
        public async Task<ActionResult<SystemSettingsDto>> UpdateSettings([FromBody] SystemSettingsDto changes) =>
            Ok(await _settingsService.UpdateAsync(changes, RequireUserId()));
    }
}
