using AutoMapper;
using BLL.Interfaces;
using BLL.Services;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ApprovalRequestsController : ControllerBase
    {
        private readonly IApprovalRequestRepository _approvalRequestRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IManagerRepository _managerRepository;
        private readonly IMapper _mapper;

        public ApprovalRequestsController(
            IApprovalRequestRepository approvalRequestRepository,
            IVolunteerRepository volunteerRepository,
            ISupplierRepository supplierRepository,
            INgoRepository ngoRepository,
            IManagerRepository managerRepository,
            IMapper mapper)
        {
            _approvalRequestRepository = approvalRequestRepository;
            _volunteerRepository = volunteerRepository;
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _managerRepository = managerRepository;
            _mapper = mapper;
        }

        // מעדכן את סטטוס האישור של הפרופיל המקושר (מתנדב / ספק / עמותה) בהתאם לסוג הבקשה
        private async Task SetProfileApprovalAsync(ApprovalRequest request, DAL.Classes.ApprovalStatus status)
        {
            switch (request.RequestedRole)
            {
                case DAL.Classes.RequestedRole.Volunteer:
                    var volunteer = await _volunteerRepository.GetByUserIdAsync(request.UserId);
                    if (volunteer != null)
                    {
                        volunteer.ApprovalStatus = status;
                        await _volunteerRepository.UpdateAsync(volunteer);
                    }
                    break;
                case DAL.Classes.RequestedRole.Supplier:
                    var supplier = await _supplierRepository.GetByUserIdAsync(request.UserId);
                    if (supplier != null)
                    {
                        supplier.ApprovalStatus = status;
                        await _supplierRepository.UpdateAsync(supplier);
                    }
                    break;
                case DAL.Classes.RequestedRole.Ngo:
                    var ngo = await _ngoRepository.GetByUserIdAsync(request.UserId);
                    if (ngo != null)
                    {
                        ngo.ApprovalStatus = status;
                        await _ngoRepository.UpdateAsync(ngo);
                    }
                    break;
            }
        }

        // מזהה את המנהל המחובר לפי הטוקן (JWT), ולא לפי קלט מהלקוח.
        // מחזיר null אם למשתמש המחובר אין פרופיל Manager בכלל.
        private async Task<Manager> GetCurrentManagerAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
                return null;

            return await _managerRepository.GetByUserIdAsync(userId);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetApprovalRequests()
        {
            var requests = await _approvalRequestRepository.GetAllAsync();
            var requestDtos = _mapper.Map<IEnumerable<ApprovalRequestResponseDto>>(requests);
            return Ok(requestDtos);
        }

        [HttpPost("{id}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveRequest(int id)
        {
            var manager = await GetCurrentManagerAsync();
            if (manager == null)
                return Unauthorized(new { message = "לא נמצא פרופיל מנהל עבור המשתמש המחובר." });

            var request = await _approvalRequestRepository.GetByIdAsync(id);
            if (request == null) return NotFound("בקשת האישור לא נמצאה.");

            request.Status = DAL.Classes.ApprovalStatus.Approved;
            request.HandledByManagerId = manager.Id;
            request.HandledAt = DateTime.UtcNow;

            await _approvalRequestRepository.UpdateAsync(request);

            await SetProfileApprovalAsync(request, DAL.Classes.ApprovalStatus.Approved);

            return Ok(new { message = "הבקשה אושרה בהצלחה והמשתמש אושר לפעילות!" });
        }

        [HttpPost("{id}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectRequest(int id, [FromBody] string rejectionReason)
        {
            var manager = await GetCurrentManagerAsync();
            if (manager == null)
                return Unauthorized(new { message = "לא נמצא פרופיל מנהל עבור המשתמש המחובר." });

            var request = await _approvalRequestRepository.GetByIdAsync(id);
            if (request == null) return NotFound("בקשת האישור לא נמצאה.");

            request.Status = DAL.Classes.ApprovalStatus.Rejected;
            request.HandledByManagerId = manager.Id;
            request.RejectionReason = rejectionReason;
            request.HandledAt = DateTime.UtcNow;

            await _approvalRequestRepository.UpdateAsync(request);

            await SetProfileApprovalAsync(request, DAL.Classes.ApprovalStatus.Rejected);

            return Ok(new { message = "הבקשה נדחתה." });
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetApprovalRequest(int id)
        {
            var r = await _approvalRequestRepository.GetByIdAsync(id);
            if (r == null) return NotFound();

            var requestDto = _mapper.Map<ApprovalRequestResponseDto>(r);
            return Ok(requestDto);
        }

        [HttpPost]
        public async Task<IActionResult> CreateApprovalRequest(
            [FromBody] CreateApprovalRequestDto requestDto,
            [FromServices] IValidationService validationService)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (passed, details) = await validationService.ValidateApplicantAsync(requestDto.IdNumber, requestDto.LicenseNumber);

            var requestEntity = _mapper.Map<ApprovalRequest>(requestDto);

            // אבטחה: הבקשה משויכת למשתמש המחובר לפי ה-JWT, לא לפי הלקוח
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();
            requestEntity.UserId = userId;

            requestEntity.Status = DAL.Classes.ApprovalStatus.Pending;
            requestEntity.CreatedAt = DateTime.UtcNow;
            requestEntity.PassedAutomaticValidation = passed;
            requestEntity.ValidationDetails = details;

            await _approvalRequestRepository.AddAsync(requestEntity);

            var responseDto = _mapper.Map<ApprovalRequestResponseDto>(requestEntity);
            return Ok(responseDto);
        }
    }
}