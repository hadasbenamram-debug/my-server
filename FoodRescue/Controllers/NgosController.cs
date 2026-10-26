using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class NgosController : ApiControllerBase
    {
        private readonly INgoService _ngoService;
        private readonly IApprovalRequestService _approvals;

        public NgosController(INgoService ngoService, IApprovalRequestService approvals)
        {
            _ngoService = ngoService;
            _approvals = approvals;
        }

        // מנהל: פרטים מלאים. מתנדב מאושר: רק עמותות מאושרות ורק השדות הדרושים למסירה. אחר: 403.
        [HttpGet]
        public async Task<IActionResult> GetNgos() =>
            Ok(await _ngoService.GetNgosForCallerAsync(CurrentUser));

        // העמותה של המשתמש המחובר
        [HttpGet("me")]
        public async Task<ActionResult<NgoResponseDto>> GetMyNgo() =>
            Ok(await _ngoService.GetMineAsync(RequireUserId()) ?? throw new NotFoundException("לא נמצא פרופיל עמותה למשתמש המחובר."));

        // מנהל והעמותה עצמה: DTO מלא. מתנדב מאושר: DTO ציבורי. אחרת 404 (בלי לחשוף שהמזהה קיים).
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetNgo(int id) =>
            Ok(await _ngoService.GetNgoForCallerAsync(id, CurrentUser) ?? throw new NotFoundException("העמותה לא נמצאה."));

        // תיקון 10: משתמש מחובר מוסיף לחשבון שלו גם תפקיד עמותה - ממתין לאישור מנהל
        [HttpPost]
        public async Task<IActionResult> CreateNgo([FromBody] CreateNgoDto dto) =>
            AddRoleResponse(await _ngoService.AddRoleAsync(RequireUserId(), dto), BLL.Services.RoleTitles.Ngo);

        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RegisterNgoWithUser([FromBody] RegisterNgoDto model)
        {
            // התשובה זהה גם כשהמייל כבר רשום (לא חושפים אילו מיילים קיימים) - הפרטים נשלחים למייל
            await _ngoService.RegisterWithUserAsync(model);
            return Message(RegistrationAcceptedMessage);
        }

        // תוקן: אישור/דחייה עוברים במנגנון האחיד - נשלח מייל, נשמר HandledByManagerId,
        // בקשת האישור מתעדכנת (גם בדחייה), והתפקיד של המשתמש הופך ל-Ngo (בעבר נשאר User).
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveNgo(int id)
        {
            await _approvals.ApproveProfileAsync(RequestedRole.Ngo, id, RequireUserId());
            return Message("העמותה אושרה בהצלחה ונשלח אליה מייל.");
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectNgo(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonElement body)
        {
            await _approvals.RejectProfileAsync(RequestedRole.Ngo, id, RequireUserId(), RejectionReason.Read(body));
            return Message("העמותה נדחתה ונשלח אליה מייל.");
        }

        [HttpPut("my-hours")]
        public async Task<IActionResult> UpdateMyNgoHours([FromBody] UpdateNgoHoursDto dto)
        {
            await _ngoService.UpdateMyHoursAsync(RequireUserId(), dto);
            return Message("שעות הקבלה והמיקום עודכנו בהצלחה.");
        }
    }

    [Route("api/admin/ngos")]
    [Authorize(Roles = "Admin")]
    public class AdminNgosController : ApiControllerBase
    {
        private readonly INgoService _ngoService;

        public AdminNgosController(INgoService ngoService)
        {
            _ngoService = ngoService;
        }

        [HttpGet]
        public async Task<ActionResult<List<AdminNgoListItemDto>>> GetNgos() =>
            Ok(await _ngoService.GetAdminListAsync());
    }
}
