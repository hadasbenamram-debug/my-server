using AutoMapper;
using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Services;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class MatchesController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;
        private readonly DonationMatchingOrchestrator _orchestrator;
        private readonly IDonationAssignmentService _donationAssignmentService;
        private readonly IDonationRepository _donationRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly MatchCreationService _matchCreationService;
        private readonly IMapper _mapper;
        private readonly MatchTimeoutService _matchTimeoutService;
        private readonly INgoSelectionService _ngoSelectionService;
        private readonly CallerProfileService _caller;
        private readonly IEmailService _emailService;
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MatchesController> _logger;

        public MatchesController(
            IMatchRepository matchRepository,
            DonationMatchingOrchestrator orchestrator,
            IDonationAssignmentService donationAssignmentService,
            IDonationRepository donationRepository,
            IVolunteerRepository volunteerRepository,
            INgoRepository ngoRepository,
            MatchCreationService matchCreationService,
            IMapper mapper,
            MatchTimeoutService matchTimeoutService,
            INgoSelectionService ngoSelectionService,
            CallerProfileService caller,
            IEmailService emailService,
            IUserRepository userRepository,
            IConfiguration configuration,
            ILogger<MatchesController> logger)
        {
            _caller = caller;
            _matchRepository = matchRepository;
            _orchestrator = orchestrator;
            _donationAssignmentService = donationAssignmentService;
            _donationRepository = donationRepository;
            _volunteerRepository = volunteerRepository;
            _ngoRepository = ngoRepository;
            _matchCreationService = matchCreationService;
            _mapper = mapper;
            _matchTimeoutService = matchTimeoutService;
            _ngoSelectionService = ngoSelectionService;
            _emailService = emailService;
            _userRepository = userRepository;
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<bool> IsParticipantAsync(CallerProfile caller, Match match)
        {
            if (caller.IsAdmin) return true;
            if (caller.OwnsMatchAsVolunteer(match) || caller.IsMatchNgo(match)) return true;
            if (caller.Supplier != null)
            {
                var donation = await _donationRepository.GetByIdAsync(match.DonationId);
                return caller.OwnsDonation(donation);
            }
            return false;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<MatchResponseDto>>> GetMatches()
        {
            var matches = await _matchRepository.GetAllAsync();
            var matchDtos = _mapper.Map<IEnumerable<MatchResponseDto>>(matches);
            return Ok(matchDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MatchResponseDto>> GetMatch(int id)
        {
            var match = await _matchRepository.GetByIdAsync(id);
            if (match == null) return NotFound(new { message = "השידוך לא נמצא." });

            var caller = await _caller.GetAsync();
            if (!await IsParticipantAsync(caller, match)) return Forbid();

            var matchDto = _mapper.Map<MatchResponseDto>(match);
            return Ok(matchDto);
        }
        [HttpPost("run-engine")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RunEngine()
        {
            _logger.LogInformation("Manual run-engine started by administrator.");
            try
            {
                // הפעלת מחזור השידוכים דרך האורקסטרטטור (יש לוודא את שם המתודה המדויק לפי מחלקת ה-Orchestrator שלך)
                await _orchestrator.RunMatchingCycleAsync();

                return Ok(new { success = true, message = "מנוע השידוכים הופעל בהצלחה." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run matching engine manually.");
                return StatusCode(500, new { message = "שגיאה פנימית בהפעלת מנוע השידוכים." });
            }
        }
        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<MatchResponseDto>>> GetActiveMatches()
        {
            var caller = await _caller.GetAsync();
            var matches = await _matchRepository.GetActiveMatchesAsync();
            if (!caller.IsAdmin)
            {
                matches = matches.Where(m =>
                    caller.OwnsMatchAsVolunteer(m) ||
                    caller.IsMatchNgo(m) ||
                    caller.OwnsDonation(m.Donation)).ToList();
            }
            var matchDtos = _mapper.Map<IEnumerable<MatchResponseDto>>(matches);
            return Ok(matchDtos);
        }

        [HttpPost("assign-donation/{donationId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignDonation(int donationId)
        {
            _logger.LogInformation("AssignDonation started for DonationId={DonationId}", donationId);

            var donation = await _donationRepository.GetByIdWithCategoriesAsync(donationId);
            if (donation == null)
            {
                return NotFound(new { message = "Donation not found" });
            }

            if (donation.Status != DonationStatus.Pending)
            {
                return Conflict(new { message = "Donation is not pending and cannot be assigned." });
            }

            var allNgos = (await _ngoRepository.GetAllAsync()).ToList();
            if (allNgos == null || allNgos.Count == 0)
            {
                return Ok(new { matched = false, message = "No NGOs configured in the system." });
            }

            var pickupDate = donation.CreatedAt.Date;
            var pickupDateTime = pickupDate + donation.LatestPickupTime;
            if (pickupDateTime > donation.ExpirationTime)
            {
                pickupDateTime = donation.ExpirationTime;
            }

            var bestMatchResult = await _ngoSelectionService.SelectBestNgoAsync(donation, allNgos, pickupDateTime);
            if (bestMatchResult == null)
            {
                return Ok(new { matched = false, message = "No NGO can receive this donation in time." });
            }

            var ngo = bestMatchResult.Value.Ngo;
            var volunteers = await _volunteerRepository.GetAvailableVolunteersAsync();

            var result = await _donationAssignmentService.AssignVolunteerAsync(donation, ngo, volunteers, DateTime.UtcNow);

            if (result == null || result.Outcome != AssignmentOutcome.Assigned)
            {
                return Ok(new { matched = false, message = "No eligible volunteer found at this time." });
            }

            var assignedVolunteer = result.AssignedVolunteer;
            var (success, createdMatch) = await _matchCreationService.CreateMatchAsync(donation, ngo, assignedVolunteer);

            if (!success)
            {
                return Conflict(new { matched = false, message = "Failed to persist match due to concurrent update. Try again." });
            }

            var user = await _userRepository.GetByIdAsync(assignedVolunteer.UserId);
            if (user != null)
            {
                try
                {
                    var jwtSettings = _configuration.GetSection("JwtSettings");
                    var secretKeyString = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret Key is missing.");
                    var secretKey = Encoding.UTF8.GetBytes(secretKeyString);

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim("MatchId", createdMatch.Id.ToString()),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                    };

                    var key = new SymmetricSecurityKey(secretKey);
                    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                    var tokenDescriptor = new JwtSecurityToken(
                        issuer: jwtSettings["Issuer"],
                        audience: jwtSettings["Audience"],
                        claims: claims,
                        expires: DateTime.UtcNow.AddMinutes(5),
                        signingCredentials: creds
                    );

                    string matchToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
                    var frontendBaseUrl = "http://localhost:5174";
                    var responseLink = $"{frontendBaseUrl}/app/volunteer/respond-match/{createdMatch.Id}?token={matchToken}";

                    await _emailService.SendEmailAsync(
                        user.Id,
                        user.Email,
                        "תרומה חדשה ממתינה לאישורך - יש לך 5 דקות!",
                        $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)}, נמצאה עבורך תרומה חדשה לאיסוף. לאישור או דחייה תוך 5 דקות לחץ על הקישור הבא: <a href='{responseLink}'>לחץ כאן לאישור/דחייה</a>"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send assignment email.");
                }
            }

            BackgroundJob.Schedule(() => _matchTimeoutService.ProcessTimeoutsAndNoShowsAsync(DateTime.UtcNow), TimeSpan.FromMinutes(5));

            return Ok(new
            {
                matched = true,
                matchId = createdMatch.Id,
                message = "Volunteer assigned successfully.",
            });
        }

        [HttpPost("{id}/accept")]
        public async Task<IActionResult> AcceptMatch(int id)
        {
            _logger.LogInformation("=== AcceptMatch started for Match ID: {MatchId} ===", id);

            var match = await _matchRepository.GetByIdAsync(id);
            if (match == null)
            {
                return NotFound(new { message = "השידוך לא נמצא." });
            }

            var caller = await _caller.GetAsync();
            if (!caller.OwnsMatchAsVolunteer(match))
            {
                return Forbid();
            }

            if (match.Status != MatchStatus.AwaitingVolunteerConfirmation)
            {
                return BadRequest(new { message = "לא ניתן לאשר שידוך זה מכיוון שאינו ממתין לאישור או שפג תוקפו." });
            }

            if (await _matchRepository.HasActiveConfirmedMatchAsync(match.VolunteerId, match.Id, match.VolunteerRouteId))
            {
                return Conflict(new { message = "למתנדב כבר יש שיבוץ פעיל אחר." });
            }

            match.Status = MatchStatus.Confirmed;

            var volunteer = match.Volunteer ?? await _volunteerRepository.GetByIdAsync(match.VolunteerId);
            if (volunteer != null)
            {
                volunteer.IsAvailable = false;
            }

            await _matchRepository.UpdateAsync(match);

            var matchDto = _mapper.Map<MatchResponseDto>(match);

            return Ok(new { success = true, message = "השידוך אושר בהצלחה.", match = matchDto });
        }

        [HttpPost("{id}/decline")]
        public async Task<IActionResult> DeclineMatch(int id)
        {
            var match = await _matchRepository.GetByIdAsync(id);
            if (match == null) return NotFound(new { message = "השידוך לא נמצא." });

            var caller = await _caller.GetAsync();
            if (!caller.OwnsMatchAsVolunteer(match)) return Forbid();

            if (match.Status != MatchStatus.AwaitingVolunteerConfirmation)
            {
                return BadRequest(new { message = "לא ניתן לדחות שידוך זה." });
            }

            match.Status = MatchStatus.Declined;

            var volunteer = match.Volunteer ?? await _volunteerRepository.GetByIdAsync(match.VolunteerId);
            if (volunteer != null)
            {
                volunteer.IsAvailable = true;
            }

            if (match.Donation != null)
            {
                match.Donation.Status = DonationStatus.Pending;
            }

            await _matchRepository.UpdateAsync(match);

            return Ok(new { success = true, message = "השידוך נדחה." });
        }
        [HttpGet("engine-status")]
        [Authorize(Roles = "Admin")]
        public IActionResult GetEngineStatus()
        {
            try
            {
                // כאן תוכל להחזיר את הסטטוס העדכני של המנוע
                return Ok(new
                {
                    status = "Ready",
                    lastRun = "לא ידוע",
                    matchedCount = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get engine status.");
                return StatusCode(500, new { message = "שגיאה בטעינת סטטוס המנוע." });
            }
        }

        [HttpPost("{id}/complete")]
        public async Task<IActionResult> SetCompleted(int id)
        {
            var match = await _matchRepository.GetByIdAsync(id);
            if (match == null) return NotFound(new { message = "השידוך לא נמצא." });

            var caller = await _caller.GetAsync();
            if (!caller.IsAdmin && !caller.OwnsMatchAsVolunteer(match)) return Forbid();

            match.Status = MatchStatus.Completed;

            if (match.Donation != null)
            {
                match.Donation.Status = DonationStatus.Delivered;
            }

            var volunteer = match.Volunteer ?? await _volunteerRepository.GetByIdAsync(match.VolunteerId);
            if (volunteer != null)
            {
                volunteer.IsAvailable = true;
            }

            await _matchRepository.UpdateAsync(match);

            return Ok(new { success = true, message = "השידוך הושלם בהצלחה!" });
        }
    }
}