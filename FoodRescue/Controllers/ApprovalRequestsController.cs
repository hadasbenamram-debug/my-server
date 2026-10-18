using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // מנגנון האישור היחיד. גם /api/volunteers|suppliers|ngos/{id}/approve מגיעים לאותו שירות.
    [Authorize]
    [Route("api/[controller]")]
    public class ApprovalRequestsController : ApiControllerBase
    {
        private readonly IApprovalRequestService _approvalRequestService;

        public ApprovalRequestsController(IApprovalRequestService approvalRequestService)
        {
            _approvalRequestService = approvalRequestService;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<ApprovalRequestResponseDto>>> GetApprovalRequests([FromQuery] PageRequest page) =>
            Paged(await _approvalRequestService.GetPageAsync(page));

        // הבקשות הממתינות, עם פרטי הפרופיל (שם, כתובת, רכב, תוקף רישיון...). ?role=Volunteer|Supplier|Ngo
        [HttpGet("pending")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<PendingApprovalRequestDto>>> GetPending([FromQuery] RequestedRole? role) =>
            Ok(await _approvalRequestService.GetPendingAsync(role));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApprovalRequestResponseDto>> GetApprovalRequest(int id) =>
            Ok(await _approvalRequestService.GetByIdAsync(id) ?? throw new NotFoundException("הבקשה לא נמצאה."));

        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveRequest(int id)
        {
            await _approvalRequestService.ApproveRequestAsync(id, RequireUserId());
            return Message("הבקשה אושרה, המשתמש אושר לפעילות ונשלח אליו מייל.");
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectRequest(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] JsonElement body)
        {
            await _approvalRequestService.RejectRequestAsync(id, RequireUserId(), RejectionReason.Read(body));
            return Message("הבקשה נדחתה ונשלח מייל למשתמש.");
        }

        // משתמש מחובר מגיש בקשה (המשתמש נלקח מה-JWT ולא מהלקוח)
        [HttpPost]
        public async Task<ActionResult<ApprovalRequestResponseDto>> CreateApprovalRequest([FromBody] CreateApprovalRequestDto requestDto) =>
            Ok(await _approvalRequestService.CreateAsync(requestDto, RequireUserId()));
    }
}
