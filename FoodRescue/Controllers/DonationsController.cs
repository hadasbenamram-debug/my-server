using AutoMapper;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Interfaces;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public class DonationsController : ControllerBase
    {

        private readonly FoodRescueDbContext _context; // הוספת ה-Context לשליפה מדויקת
        private readonly IDonationRepository _donationRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly CallerProfileService _caller;
        private readonly IMapper _mapper;

        public DonationsController(
               IDonationRepository donationRepository,
               ISupplierRepository supplierRepository,
               INgoRepository ngoRepository,
               IMatchRepository matchRepository,
               CallerProfileService caller,
               IMapper mapper,
               FoodRescueDbContext context)
        {
            _caller = caller;
            _donationRepository = donationRepository;
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _matchRepository = matchRepository;
            _mapper = mapper;
            _context = context;
        }

        [HttpGet("my-supplier-donations")]
        public async Task<IActionResult> GetMySupplierDonations()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var supplier = await _supplierRepository.GetByUserIdAsync(userId);
            if (supplier == null)
                return NotFound(new { message = "לא נמצא פרופיל ספק עבור משתמש זה." });

            var donations = await _context.Donations
                .Include(d => d.Region)
                .Where(d => d.SupplierId == supplier.Id)
                .ToListAsync();

            var donationDtos = donations.Select(d => new
            {
                id = d.Id,
                status = d.Status.ToString(),
                regionId = d.RegionId,
                regionName = d.Region != null ? d.Region.RegionName : "אזור כללי",
                priorityScore = d.PriorityScore,
                createdAt = d.CreatedAt
            });

            return Ok(donationDtos);
        }

        [HttpGet("my-ngo-donations")]
        public async Task<ActionResult<IEnumerable<DonationResponseDto>>> GetMyNgoDonations()
        {
            var caller = await _caller.GetAsync();
            if (caller == null)
                return Unauthorized();

            if (caller.IsAdmin)
            {
                var donations = await _donationRepository.GetAllWithCategoriesAsync();
                return Ok(_mapper.Map<IEnumerable<DonationResponseDto>>(donations));
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var ngo = await _ngoRepository.GetByUserIdAsync(userId);
            if (ngo == null)
                return NotFound(new { message = "לא נמצא פרופיל עמותה המשויך למשתמש זה." });

            var matches = await _matchRepository.GetAllAsync();
            var ngoDonationIds = matches.Where(m => m.NgoId == ngo.Id).Select(m => m.DonationId).ToList();

            var allDonations = await _donationRepository.GetAllWithCategoriesAsync();
            var ngoDonations = allDonations.Where(d => ngoDonationIds.Contains(d.Id)).ToList();

            var donationDtos = _mapper.Map<IEnumerable<DonationResponseDto>>(ngoDonations);
            return Ok(donationDtos);
        }

        [HttpPut("{id}/cancel")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CancelDonationAsAdmin(int id)
        {
            var donation = await _donationRepository.GetByIdAsync(id);
            if (donation == null)
            {
                return NotFound(new { message = "התרומה לא נמצאה." });
            }

            donation.Status = DonationStatus.Cancelled;

            try
            {
                await _donationRepository.UpdateAsync(donation);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "שגיאת עדכון במקביל, נסה שוב." });
            }

            return Ok(new { message = "התרומה בוטלה בהצלחה בהתערבות מנהל." });
        }
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetDonations()
        {
            // שליפה ישירה הכוללת טעינה של הספק המקושר
            var donations = await _context.Donations
                .Include(d => d.Supplier)
                .ToListAsync();

            var donationDtos = donations.Select(d => new
            {
                id = d.Id,
                status = d.Status.ToString(),
                supplierId = d.SupplierId,
                supplierName = d.Supplier != null ? d.Supplier.BusinessName : "לא ידוע",
                createdAt = d.CreatedAt,
                expirationTime = d.ExpirationTime,
                urgencyScore = d.UrgencyScore,
                priorityScore = d.PriorityScore
            });

            return Ok(donationDtos);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<DonationResponseDto>> GetDonation(int id)
        {
            var d = await _donationRepository.GetByIdWithCategoriesAsync(id);
            if (d == null) return NotFound(new { message = "התרומה לא נמצאה." });

            var caller = await _caller.GetAsync();
            if (!caller.IsAdmin && !caller.OwnsDonation(d) && !caller.IsApprovedVolunteer)
                return Forbid();

            var donationDto = _mapper.Map<DonationResponseDto>(d);
            return Ok(donationDto);
        }

        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<DonationResponseDto>>> GetAvailableDonations()
        {
            var caller = await _caller.GetAsync();
            if (!caller.IsAdmin && !caller.IsApprovedVolunteer) return Forbid();

            var donations = await _donationRepository.GetAvailableDonationsAsync();
            var donationDtos = _mapper.Map<IEnumerable<DonationResponseDto>>(donations);
            return Ok(donationDtos);
        }

        [HttpPost]
        public async Task<ActionResult<DonationResponseDto>> CreateDonation([FromBody] CreateDonationDto donationDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (donationDto.Categories == null || !donationDto.Categories.Any())
            {
                return BadRequest(new { message = "יש לספק לפחות שורת קטגוריה אחת עבור התרומה." });
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            Supplier supplier;
            if (User.IsInRole("Admin"))
            {
                supplier = await _supplierRepository.GetByIdAsync(donationDto.SupplierId);
                if (supplier == null)
                    return BadRequest(new { message = "הספק שצוין לא נמצא." });
            }
            else
            {
                supplier = await _supplierRepository.GetByUserIdAsync(userId);
                if (supplier == null)
                    return StatusCode(403, new { message = "רק משתמש עם פרופיל ספק יכול ליצור תרומה." });
            }

            if (supplier.ApprovalStatus != DAL.Classes.ApprovalStatus.Approved)
                return StatusCode(403, new { message = "הספק טרם אושר על ידי מנהל, ולכן לא ניתן ליצור תרומות." });

            if (donationDto.Categories.Select(c => c.CategoryId).Distinct().Count() != donationDto.Categories.Count)
                return BadRequest(new { message = "אין לציין את אותה קטגוריה יותר מפעם אחת בתרומה." });

            var now = DateTime.UtcNow;

            var donationEntity = _mapper.Map<Donation>(donationDto);

            donationEntity.ExpirationTime = now.AddHours(24);
            donationEntity.Id = 0;
            donationEntity.SupplierId = supplier.Id;
            donationEntity.CreatedAt = now;

            double urgencyConstant = 120.0;
            donationEntity.PriorityScore = PriorityCalculator.Calculate(donationEntity, now, urgencyConstant);

            var hoursToExpiration = (donationEntity.ExpirationTime - now).TotalHours;
            donationEntity.UrgencyScore = hoursToExpiration > 0 ? Math.Round(100 / hoursToExpiration, 2) : 100;
            donationEntity.Status = DonationStatus.Pending;

            await _donationRepository.AddAsync(donationEntity);

            var resultDto = _mapper.Map<DonationResponseDto>(donationEntity);
            return Ok(resultDto);
        }

        [HttpPost("{id}/claim")]
        public async Task<IActionResult> ClaimDonation(int id)
        {
            var caller = await _caller.GetAsync();
            if (!caller.IsApprovedVolunteer) return Forbid();

            var donation = await _donationRepository.GetByIdAsync(id);
            if (donation == null)
            {
                return NotFound(new { message = "התרומה לא נמצאה." });
            }

            if (donation.Status != DonationStatus.Pending)
            {
                return Conflict(new { message = "התרומה כבר אינה זמינה (נלקחה על ידי משתמש אחר)." });
            }

            donation.Status = DonationStatus.Matched;

            try
            {
                await _donationRepository.UpdateAsync(donation);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "התרומה עודכנה במקביל על ידי מתנדב אחר. נסה שוב." });
            }

            return Ok(new { message = "התרומה נתפסה בהצלחה והוקצתה אליך!" });
        }
    }
}