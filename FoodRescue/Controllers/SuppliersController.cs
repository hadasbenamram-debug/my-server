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
    public class SuppliersController : ApiControllerBase
    {
        private readonly ISupplierService _supplierService;
        private readonly IApprovalRequestService _approvals;

        public SuppliersController(ISupplierService supplierService, IApprovalRequestService approvals)
        {
            _supplierService = supplierService;
            _approvals = approvals;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<SupplierResponseDto>>> GetSuppliers() =>
            Ok(await _supplierService.GetAllAsync());

        [HttpGet("current")]
        public async Task<ActionResult<SupplierResponseDto>> GetCurrentSupplier() =>
            Ok(await _supplierService.GetCurrentAsync(RequireUserId())
               ?? throw new NotFoundException("לא נמצא פרופיל ספק המשוייך למשתמש המחובר."));

        // אותה תשובה גם כשלא קיים וגם כשאין הרשאה - כדי לא לחשוף שהמזהה קיים.
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierResponseDto>> GetSupplier(int id) =>
            Ok(await _supplierService.GetByIdAsync(id, CurrentUser) ?? throw new NotFoundException("הספק לא נמצא."));

        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<ActionResult<SupplierResponseDto>> CreateSupplier([FromForm] CreateSupplierDto supplierDto) =>
            Ok(await _supplierService.CreateAsync(RequireUserId(), supplierDto));

        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> RegisterSupplierWithUser([FromForm] RegisterSupplierDto model)
        {
            var supplier = await _supplierService.RegisterWithUserAsync(model);
            return Ok(new { message = "הרישום בוצע בהצלחה! סיסמה זמנית נשלחה לכתובת האימייל שלך.", data = supplier });
        }

        // תוקן: האישור מחפש לפי מזהה ספק בלבד (בעבר: אם לא נמצא ספק - ניסה כמזהה משתמש, ויכול היה לאשר ספק אחר)
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveSupplier(int id)
        {
            await _approvals.ApproveProfileAsync(RequestedRole.Supplier, id, RequireUserId());
            return Message("הספק אושר בהצלחה ומייל נשלח אליו.");
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectSupplier(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonElement body)
        {
            await _approvals.RejectProfileAsync(RequestedRole.Supplier, id, RequireUserId(), RejectionReason.Read(body));
            return Message("הבקשה נדחתה ונשלח מייל לספק.");
        }
    }

    [Route("api/admin/suppliers")]
    [Authorize(Roles = "Admin")]
    public class AdminSuppliersController : ApiControllerBase
    {
        private readonly ISupplierService _supplierService;

        public AdminSuppliersController(ISupplierService supplierService)
        {
            _supplierService = supplierService;
        }

        [HttpGet]
        public async Task<ActionResult<List<AdminSupplierListItemDto>>> GetSuppliers() =>
            Ok(await _supplierService.GetAdminListAsync());

        // הורדת קובץ רישיון העסק - מנהל בלבד (הקבצים לא נמצאים תחת wwwroot)
        [HttpGet("{id:int}/license")]
        public async Task<IActionResult> GetLicense(int id)
        {
            var file = await _supplierService.GetLicenseFileAsync(id) ?? throw new NotFoundException("קובץ הרישיון לא נמצא.");
            return PhysicalFile(file.FullPath, file.ContentType, file.DownloadName);
        }
    }
}
