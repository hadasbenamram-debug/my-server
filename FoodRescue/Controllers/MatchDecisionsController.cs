using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // תיקון 75: "למה המנוע החליט כך" - ההסבר שנשמר לכל מסלול שהמנוע יצר (למנהל בלבד)
    [Authorize(Roles = "Admin")]
    [Route("api/matches/decisions")]
    public class MatchDecisionsController : ApiControllerBase
    {
        private const string NoExplanation = "אין הסבר שמור למסלול הזה. הסברים נשמרים רק למסלולים שנוצרו אחרי העדכון.";

        private readonly IRouteDecisionService _decisions;

        public MatchDecisionsController(IRouteDecisionService decisions)
        {
            _decisions = decisions;
        }

        // ההחלטות האחרונות של המנוע (ברירת מחדל: 20)
        [HttpGet]
        public async Task<ActionResult<List<RouteDecisionSummaryDto>>> GetRecent([FromQuery] int take = 20) =>
            Ok(await _decisions.GetRecentAsync(take));

        [HttpGet("{routeId:int}")]
        public async Task<ActionResult<RouteDecisionDto>> Get(int routeId) =>
            Ok(await _decisions.GetAsync(routeId) ?? throw new NotFoundException(NoExplanation));

        // מסך ניהול התרומות: "למה נבחר המתנדב?" לפי מספר התרומה
        [HttpGet("by-donation/{donationId:int}")]
        public async Task<ActionResult<DecisionLookupDto>> FindByDonation(int donationId)
        {
            var routeId = await _decisions.FindRouteForDonationAsync(donationId);
            return routeId.HasValue
                ? Ok(new DecisionLookupDto { RouteId = routeId.Value })
                : throw new NotFoundException(NoExplanation);
        }
    }
}
