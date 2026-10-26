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

        private const string ActiveRoutePath = "/app/volunteer/active-route";

        private readonly IMatchService _matchService;
        private readonly IUserService _userService;

        public MatchesController(IMatchService matchService, IUserService userService)
        {
            _matchService = matchService;
            _userService = userService;
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

        // תיקון 16: היסטוריית השיבוצים של המתנדב המחובר (גם שיבוצים שכבר הסתיימו), מהחדש לישן
        [HttpGet("my-history")]
        public async Task<ActionResult<List<MatchResponseDto>>> GetMyHistory() =>
            Ok(await _matchService.GetMyVolunteerHistoryAsync(CurrentUser));

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
        public async Task<ActionResult<RouteDto>> AcceptMatch(int id)
        {
            var outcome = await _matchService.AcceptAsync(id, CurrentUser);
            return outcome.Result == RouteResponseResult.Done ? Ok(outcome.Route) : ResponseProblem(outcome.Result);
        }

        [HttpPost("{id:int}/decline")]
        public async Task<IActionResult> DeclineMatch(int id)
        {
            var result = await _matchService.DeclineAsync(id, CurrentUser);
            return result == RouteResponseResult.Done
                ? Message("הבקשה נדחתה. התרומות יועברו למתנדב אחר.")
                : ResponseProblem(result);
        }

        // תיקון 12: אישור / דחייה שכבר לא אפשריים (למשל אחרי שזמן התגובה עבר) - תשובה ברורה, בלי חריגה.
        // בעבר נזרקו כאן חריגות, ומתנדב שנכנס אחרי 5 הדקות "הפיל" את השרת בהרצה מ-Visual Studio.
        private ObjectResult ResponseProblem(RouteResponseResult result)
        {
            var (code, message) = result switch
            {
                RouteResponseResult.Expired =>
                    ("Expired", "זמן התגובה לבקשה הזו הסתיים, והיא הועברה למתנדב אחר. תודה על הנכונות!"),
                RouteResponseResult.NotAwaiting =>
                    ("NotAwaiting", "הבקשה כבר לא ממתינה לתגובה: היא כבר נענתה, או שזמן התגובה הסתיים והיא הועברה למתנדב אחר."),
                RouteResponseResult.LicenseInvalid =>
                    ("LicenseInvalid", "לא ניתן לאשר: " + BLL.Algorithms.DriverLicensePolicy.NotValidMessage +
                                       " הבקשה הועברה למתנדב אחר. לעדכון תוקף הרישיון יש לפנות למנהל המערכת."),
                RouteResponseResult.VolunteerBusy =>
                    ("VolunteerBusy", "יש לך כבר מסלול פעיל אחר. אפשר לאשר מסלול חדש אחרי שמסיימים את הנוכחי."),
                _ => ("LinkInvalid", "הקישור כבר לא בתוקף: זמן התגובה הסתיים או שכבר השתמשו בו.")
            };

            return StatusCode(409, new Infrastructure.ApiError { Status = 409, Code = code, Message = message });
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
        public async Task<ActionResult<RouteDto>> GetRouteByLink([FromBody] RespondByLinkDto dto)
        {
            var route = await _matchService.GetRouteByLinkAsync(dto.Token);
            return route != null ? Ok(route) : ResponseProblem(RouteResponseResult.LinkInvalid);
        }

        [HttpPost("respond-by-link")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<ActionResult<RespondByLinkResultDto>> RespondByLink([FromBody] RespondByLinkDto dto)
        {
            var outcome = await _matchService.RespondByLinkAsync(dto);
            if (outcome.Result != RouteResponseResult.Done || outcome.Route == null)
                return ResponseProblem(outcome.Result);

            // תיקון 11: בעבר אחרי אישור מהקישור הופיע "התחבר/י למערכת" - עכשיו המתנדב נכנס ישר למסלול הפעיל
            var session = dto.Accept
                ? await _userService.CreateLinkSessionAsync(outcome.VolunteerUserId, RequestedRole.Volunteer)
                : null;

            return Ok(new RespondByLinkResultDto
            {
                Route = outcome.Route,
                Session = session,
                NextPath = dto.Accept ? ActiveRoutePath : null
            });
        }
    }
}
