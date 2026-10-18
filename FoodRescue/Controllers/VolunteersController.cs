using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using RequestedRole = DAL.Classes.RequestedRole;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class VolunteersController : ApiControllerBase
    {
        private readonly IVolunteerService _volunteerService;
        private readonly IApprovalRequestService _approvals;

        public VolunteersController(IVolunteerService volunteerService, IApprovalRequestService approvals)
        {
            _volunteerService = volunteerService;
            _approvals = approvals;
        }

        // ---------------- המתנדב המחובר ----------------

        [HttpGet("availability")]
        public async Task<ActionResult<List<AvailabilitySlotResponseDto>>> GetMyAvailability() =>
            Ok(await _volunteerService.GetMyAvailabilityAsync(RequireUserId()));

        // שמירת כל משבצות הזמינות (מחליף את הקיימות). נבדק: התחלה לפני סיום, אזור קיים, אזור יעד שונה מאזור המוצא.
        [HttpPost("availability")]
        public async Task<IActionResult> SaveMyAvailability([FromBody] List<CreateAvailabilitySlotDto> slotsDtos)
        {
            await _volunteerService.SaveMyAvailabilityAsync(RequireUserId(), slotsDtos);
            return Message("הזמינות נשמרה בהצלחה.");
        }

        // מיקום חי (גם דרך SignalR: NotificationHub.UpdateLocation)
        [HttpPut("me/location")]
        public async Task<IActionResult> UpdateMyLocation([FromBody] LocationUpdateDto dto)
        {
            await _volunteerService.UpdateMyLocationAsync(RequireUserId(), dto.Latitude, dto.Longitude);
            return NoContent();
        }

        // ---------------- מנהל ----------------

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<AdminVolunteerListItemDto>>> GetVolunteers() =>
            Ok(await _volunteerService.GetAllForAdminAsync());

        // אישור / דחייה: אותו מנגנון כמו /api/approvalrequests (מייל, HandledByManagerId, עדכון הבקשה)
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveVolunteer(int id)
        {
            await _approvals.ApproveProfileAsync(RequestedRole.Volunteer, id, RequireUserId());
            return Message("המתנדב אושר בהצלחה ומייל נשלח אליו.");
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectVolunteer(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonElement body)
        {
            await _approvals.RejectProfileAsync(RequestedRole.Volunteer, id, RequireUserId(), RejectionReason.Read(body));
            return Message("הבקשה נדחתה ונשלח מייל למתנדב.");
        }

        // ---------------- לפי מזהה (המתנדב עצמו או מנהל) ----------------

        [HttpGet("{id:int}")]
        public async Task<ActionResult<VolunteerResponseDto>> GetVolunteer(int id) =>
            Ok(await _volunteerService.GetByIdAsync(id, CurrentUser) ?? throw new NotFoundException("המתנדב לא נמצא."));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateVolunteer(int id, [FromBody] UpdateVolunteerDto volunteerDto)
        {
            await _volunteerService.UpdateAsync(id, volunteerDto, CurrentUser);
            return Message("פרטי המתנדב עודכנו בהצלחה.");
        }

        [HttpGet("{id:int}/availability")]
        public async Task<ActionResult<List<AvailabilitySlotResponseDto>>> GetVolunteerAvailability(int id) =>
            Ok(await _volunteerService.GetAvailabilityAsync(id, CurrentUser));

        [HttpPost("{id:int}/availability")]
        public async Task<IActionResult> SaveVolunteerAvailability(int id, [FromBody] List<CreateAvailabilitySlotDto> slotsDtos)
        {
            await _volunteerService.SaveAvailabilityAsync(id, slotsDtos, CurrentUser);
            return Message("הזמינות נשמרה בהצלחה.");
        }

        // ---------------- יצירה / הרשמה ----------------

        // משתמש מחובר יוצר לעצמו פרופיל מתנדב
        [HttpPost]
        public async Task<ActionResult<VolunteerResponseDto>> CreateVolunteer([FromBody] CreateVolunteerDto volunteerDto) =>
            Ok(await _volunteerService.CreateAsync(RequireUserId(), volunteerDto));

        // הרשמה מלאה (משתמש + פרופיל) - סיסמה זמנית נשלחת במייל וחייבים להחליף אותה בכניסה הראשונה
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RegisterVolunteerWithUser([FromBody] RegisterVolunteerDto model)
        {
            await _volunteerService.RegisterWithUserAsync(model);
            return Message("הרישום בוצע בהצלחה! סיסמה זמנית נשלחה לכתובת האימייל שלך.");
        }
    }

    // דוחות ונתוני דשבורד למנהל.
    // הוסרו מכאן approve/reject הכפולים (בלי מייל) - נשאר מנגנון אישור אחד.
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ApiControllerBase
    {
        private readonly IAdminReportService _reportService;

        public AdminController(IAdminReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("volunteers/reports")]
        public async Task<ActionResult<SystemReportDto>> GetSystemReports() =>
            Ok(await _reportService.GetSystemReportAsync());

        [HttpGet("dashboard-stats")]
        public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats() =>
            Ok(await _reportService.GetDashboardStatsAsync());

        [HttpGet("volunteers/pending-requests")]
        public async Task<ActionResult<List<PendingApprovalRequestDto>>> GetPendingRequests() =>
            Ok(await _reportService.GetPendingVolunteerRequestsAsync());

        [HttpGet("volunteers/availability")]
        public async Task<ActionResult<List<VolunteerAvailabilitySummaryDto>>> GetVolunteersAvailability() =>
            Ok(await _reportService.GetVolunteersAvailabilityAsync());

        // מוניטור פעילות בשטח - מסלול פעיל לכל מתנדב עם המיקום האחרון שלו
        [HttpGet("live-monitor")]
        public async Task<ActionResult<List<LiveMissionDto>>> GetLiveMonitor() =>
            Ok(await _reportService.GetLiveMonitorAsync());
    }
}
