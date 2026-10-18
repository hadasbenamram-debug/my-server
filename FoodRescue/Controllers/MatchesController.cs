using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // שידוכים ומסלולים. כל ההחלטות וההרשאות נמצאות ב-MatchService / RouteWorkflowService.
    // מחזור החיים: AwaitingVolunteerConfirmation -> Confirmed -> InPickup -> InTransit -> Delivered -> Completed
    // (ובצד: Declined / TimedOut / NoShow / Cancelled -> שיבוץ מחדש אוטומטי)
    [Authorize]
    [Route("api/[controller]")]
    public class MatchesController : ApiControllerBase
    {
        private const string MatchNotFound = "השידוך לא נמצא.";
        private const string RouteNotFound = "המסלול לא נמצא.";

        private readonly IMatchService _matchService;

        public MatchesController(IMatchService matchService)
        {
            _matchService = matchService;
        }

        // ---------------- קריאה ----------------

        // כל השידוכים - מנהל בלבד, בעימוד ועם סינון לפי סטטוס (?status=NoShow)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<MatchResponseDto>>> GetMatches([FromQuery] PageRequest page, [FromQuery] MatchStatus? status) =>
            Paged(await _matchService.GetPageAsync(page, status));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<MatchResponseDto>> GetMatch(int id) =>
            // אותה תשובה גם כשלא קיים וגם כשאין הרשאה - כדי לא לחשוף שהשידוך קיים.
            Ok(await _matchService.GetByIdAsync(id, CurrentUser) ?? throw new NotFoundException(MatchNotFound));

        [HttpGet("active")]
        public async Task<ActionResult<List<MatchResponseDto>>> GetActiveMatches() =>
            Ok(await _matchService.GetActiveAsync(CurrentUser));

        // ---------------- המנוע (מנהל) ----------------

        // הפעלה ידנית של אותו מנוע שרץ לפי לוח הזמנים - שומר שידוכים ומסלולים בפועל
        [HttpPost("run-engine")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<EngineRunResultDto>> RunEngine()
        {
            var result = await _matchService.RunEngineAsync();
            return result.Started ? Ok(result) : Conflict(result);
        }

        [HttpGet("engine-status")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<EngineStatusDto>> GetEngineStatus() =>
            Ok(await _matchService.GetEngineStatusAsync());

        // שיבוץ ידני של תרומה אחת - דרך אותו מנוע (שידוך אחד, מייל אחד)
        [HttpPost("assign-donation/{donationId:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<AssignDonationResultDto>> AssignDonation(int donationId) =>
            Ok(await _matchService.AssignDonationAsync(donationId) ?? throw new NotFoundException("התרומה לא נמצאה."));

        // ---------------- תגובת המתנדב (לכל המסלול) ----------------

        // אישור אחד לכל המסלול (בעבר אישור נפרד לכל תחנה)
        [HttpPost("{id:int}/accept")]
        public async Task<ActionResult<RouteDto>> AcceptMatch(int id) =>
            Ok(await _matchService.AcceptAsync(id, CurrentUser));

        [HttpPost("{id:int}/decline")]
        public async Task<IActionResult> DeclineMatch(int id)
        {
            await _matchService.DeclineAsync(id, CurrentUser);
            return Message("הבקשה נדחתה. התרומות יועברו למתנדב אחר.");
        }

        // ---------------- ביצוע המסלול ----------------

        // המתנדב יוצא לדרך (המסלול עובר ל-InProgress, השידוכים ל-InPickup)
        [HttpPost("routes/{routeId:int}/start")]
        public async Task<ActionResult<RouteDto>> StartRoute(int routeId) =>
            Ok(await _matchService.StartRouteAsync(routeId, CurrentUser));

        // נאסף מהספק (InPickup -> InTransit, התרומה -> PickedUp)
        [HttpPost("{id:int}/picked-up")]
        public async Task<IActionResult> MarkPickedUp(int id)
        {
            await _matchService.PickUpAsync(id, CurrentUser);
            return Message("האיסוף נרשם.");
        }

        // נמסר לעמותה (InTransit -> Delivered). העמותה מאשרת קבלה דרך /api/donations/{id}/confirm-receipt
        [HttpPost("{id:int}/complete")]
        public async Task<IActionResult> SetCompleted(int id)
        {
            await _matchService.CompleteAsync(id, CurrentUser);
            return Message("המסירה נרשמה. העמותה תתבקש לאשר את הקבלה.");
        }

        // No-Show (מנהל): המתנדב לא הגיע. התרומות חוזרות לשיבוץ מחדש מיידי וציון האמינות שלו יורד.
        [HttpPost("{id:int}/no-show")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> MarkNoShow(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NoShowDto? dto)
        {
            var count = await _matchService.MarkNoShowAsync(id, dto?.Reason);
            return Ok(new { message = $"סומן No-Show. {count} תרומות הוחזרו לשיבוץ מחדש.", requeued = count });
        }

        // ---------------- מסלולים ----------------

        [HttpGet("routes/my-active")]
        public async Task<ActionResult<RouteDto>> GetMyActiveRoute()
        {
            var route = await _matchService.GetMyActiveRouteAsync(CurrentUser);
            return route == null ? NoContent() : Ok(route);
        }

        [HttpGet("routes/{routeId:int}")]
        public async Task<ActionResult<RouteDto>> GetRoute(int routeId) =>
            Ok(await _matchService.GetRouteAsync(routeId, CurrentUser) ?? throw new NotFoundException(RouteNotFound));

        // ---------------- קישור מהמייל (בלי התחברות) ----------------
        // תוקן: הקישור הכיל JWT מלא של המתנדב (כולל גישה לכל המערכת) ו-MatchId שלא נבדק.
        // עכשיו: טוקן חד-פעמי אקראי, שמור מגובב במסד, מוגבל למסלול אחד ולתגובה אחת. הטוקן נשלח בגוף הבקשה ולא ב-URL.

        [HttpPost("by-link")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<ActionResult<RouteDto>> GetRouteByLink([FromBody] RespondByLinkDto dto) =>
            Ok(await _matchService.GetRouteByLinkAsync(dto.Token)
               ?? throw new NotFoundException("הקישור אינו בתוקף (פג, כבר נוצל או שגוי)."));

        [HttpPost("respond-by-link")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<ActionResult<RouteDto>> RespondByLink([FromBody] RespondByLinkDto dto) =>
            Ok(await _matchService.RespondByLinkAsync(dto));
    }
}
