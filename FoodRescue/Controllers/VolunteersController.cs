using AutoMapper;
using AutoMapper.QueryableExtensions;
using BLL.Algorithms;
using BLL.Interfaces;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class VolunteersController : ControllerBase
    {
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IUserRepository _userRepository;
        private readonly FoodRescueDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<VolunteersController> _logger;

        public VolunteersController(
            IVolunteerRepository volunteerRepository,
            IUserRepository userRepository,
            FoodRescueDbContext context,
            IEmailService emailService,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<VolunteersController> logger)
        {
            _volunteerRepository = volunteerRepository;
            _userRepository = userRepository;
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
        }

        public class RegisterVolunteerModel
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string VehicleType { get; set; }
            public decimal MaxCapacityKg { get; set; }
            public string IdNumber { get; set; }
            public string DriverLicenseNumber { get; set; }
            public DateTime DateOfBirth { get; set; }
        }

        private async Task<int?> GetVolunteerIdFromUserAsync(int requestedId)
        {
            if (User.IsInRole("Admin"))
            {
                return requestedId;
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                return null;
            }

            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
            return volunteer?.Id;
        }
        [HttpGet("availability")]
        public async Task<ActionResult<IEnumerable<AvailabilitySlotResponseDto>>> GetMyAvailability()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();

            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
            if (volunteer == null) return NotFound(new { message = "המתנדב לא נמצא." });

            // אין צורך ב-Include! ה-ProjectTo מטפל בחיבור הטבלאות אוטומטית.
            var dtos = await _context.AvailabilitySlots
                .Where(s => s.VolunteerId == volunteer.Id)
                .ProjectTo<AvailabilitySlotResponseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return Ok(dtos);
        }

        [HttpPost("availability")]
        public async Task<IActionResult> SaveMyAvailability([FromBody] List<CreateAvailabilitySlotDto> slotsDtos)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (slotsDtos == null) return BadRequest(new { message = "נתוני הזמינות ריקים." });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();

            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
            if (volunteer == null) return NotFound(new { message = "המתנדב לא נמצא." });

            var existingSlots = await _context.AvailabilitySlots
                .Where(s => s.VolunteerId == volunteer.Id)
                .ToListAsync();

            _context.AvailabilitySlots.RemoveRange(existingSlots);

            foreach (var dto in slotsDtos)
            {
                if (!TimeSpan.TryParse(dto.StartTime, out TimeSpan startTime))
                {
                    if (DateTime.TryParse(dto.StartTime, out DateTime dtStart))
                    {
                        startTime = dtStart.TimeOfDay;
                    }
                    else
                    {
                        return BadRequest(new { message = $"פורמט שעת התחלה אינו תקין: {dto.StartTime}" });
                    }
                }

                if (!TimeSpan.TryParse(dto.EndTime, out TimeSpan endTime))
                {
                    if (DateTime.TryParse(dto.EndTime, out DateTime dtEnd))
                    {
                        endTime = dtEnd.TimeOfDay;
                    }
                    else
                    {
                        return BadRequest(new { message = $"פורמט שעת סיום אינו תקין: {dto.EndTime}" });
                    }
                }

                var slot = new AvailabilitySlot
                {
                    VolunteerId = volunteer.Id,
                    RegionId = dto.RegionId,
                    ToRegionId = dto.ToRegionId > 0 ? dto.ToRegionId : (int?)null,
                    Day = (DayOfWeek)dto.Day,
                    StartTime = startTime,
                    EndTime = endTime
                };
                _context.AvailabilitySlots.Add(slot);
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "הזמינות נשמרה בהצלחה." });
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetVolunteers()
        {
            // אין צורך ב-Include! ה-ProjectTo מטפל בחיבור הטבלאות אוטומטית.
            var volunteerDtos = await _context.Volunteers
                .ProjectTo<VolunteerResponseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return Ok(volunteerDtos);
        }

        [HttpPost("{id}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveVolunteer(int id)
        {
            var volunteer = await _volunteerRepository.GetByIdAsync(id);
            if (volunteer == null)
            {
                return NotFound(new { message = "המתנדב לא נמצא" });
            }

            var user = await _userRepository.GetByIdAsync(volunteer.UserId);
            if (user == null)
            {
                return NotFound(new { message = "המשתמש המקושר לא נמצא" });
            }

            volunteer.ApprovalStatus = DAL.Classes.ApprovalStatus.Approved;
            await _volunteerRepository.UpdateAsync(volunteer);

            var approvalReq = await _context.ApprovalRequests
                .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RequestedRole == DAL.Classes.RequestedRole.Volunteer);
            if (approvalReq != null)
            {
                approvalReq.Status = DAL.Classes.ApprovalStatus.Approved;
                approvalReq.HandledAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKeyString = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret Key is missing in configuration.");
            var secretKey = Encoding.UTF8.GetBytes(secretKeyString);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(secretKey);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(3),
                signingCredentials: creds
            );

            string userToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

            string frontendAvailabilityUrl = $"http://localhost:5174/app/volunteer/availability?token={userToken}";

            string emailSubject = "אישור הצטרפות למערכת ומילוי זמינות";
            string emailBody = $@"
                שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/><br/>
                שמחים לעדכן כי בקשת ההצטרפות שלך למערכת <b>אושרה על ידי המנהל!</b><br/>
                כדי שנוכל להתאים לך משימות בצורה הטובה ביותר, עליך להגדיר את שעות וימי הזמינות שלך.<br/><br/>
                לחץ על הקישור הבא כדי להיכנס למערכת באופן אוטומטי:<br/>
                <a href='{frontendAvailabilityUrl}' target='_blank'>מעבר לעמוד ניהול זמינות</a><br/><br/>
                תודה רבה!
            ";

            await _emailService.SendEmailAsync(user.Id, user.Email, emailSubject, emailBody);
            // או בהתאם לסדר הפרמטרים החדש בממשק שלך, למשל אם נוסף פרמטר כמו userId בהתחלה

            return Ok(new { message = "המתנדב אושר בהצלחה ומייל נשלח אליו." });
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<VolunteerResponseDto>> GetVolunteer(int id)
        {
            var targetId = await GetVolunteerIdFromUserAsync(id);
            if (targetId == null) return Forbid();

            var volunteer = await _volunteerRepository.GetByIdAsync(targetId.Value);
            if (volunteer == null) return NotFound();

            if (!User.IsInRole("Admin"))
            {
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (currentUserIdClaim == null || volunteer.UserId.ToString() != currentUserIdClaim)
                {
                    return Forbid();
                }
            }

            var volunteerDto = _mapper.Map<VolunteerResponseDto>(volunteer);

            if (!User.IsInRole("Admin") && !string.IsNullOrEmpty(volunteerDto.IdNumber))
            {
                volunteerDto.IdNumber = "***-***-" + volunteerDto.IdNumber.Substring(Math.Max(0, volunteerDto.IdNumber.Length - 3));
            }

            return Ok(volunteerDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateVolunteer(int id, [FromBody] VolunteerResponseDto volunteerDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var targetId = await GetVolunteerIdFromUserAsync(id);
            if (targetId == null) return Forbid();

            var volunteer = await _volunteerRepository.GetByIdAsync(targetId.Value);
            if (volunteer == null) return NotFound();

            if (!User.IsInRole("Admin"))
            {
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (currentUserIdClaim == null || volunteer.UserId.ToString() != currentUserIdClaim)
                {
                    return Forbid();
                }
            }

            _mapper.Map(volunteerDto, volunteer);

            await _volunteerRepository.UpdateAsync(volunteer);
            return Ok(new { message = "פרטי המתנדב עודכנו בהצלחה." });
        }

        [HttpPost]
        public async Task<ActionResult<VolunteerResponseDto>> CreateVolunteer([FromBody] CreateVolunteerDto volunteerDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return Unauthorized(new { message = "המשתמש המחובר לא נמצא." });

            var existing = await _volunteerRepository.GetByUserIdAsync(userId);
            if (existing != null)
                return Conflict(new { message = "למשתמש זה כבר קיים פרופיל מתנדב." });

            var volunteerEntity = _mapper.Map<Volunteer>(volunteerDto);

            volunteerEntity.UserId = userId;
            volunteerEntity.ApprovalStatus = DAL.Classes.ApprovalStatus.Pending;

            await _volunteerRepository.AddAsync(volunteerEntity);

            var approvalRequest = new ApprovalRequest
            {
                UserId = userId,
                RequestedRole = DAL.Classes.RequestedRole.Volunteer,
                Status = DAL.Classes.ApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _context.ApprovalRequests.Add(approvalRequest);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(user.Email))
            {
                try
                {
                    await _emailService.SendEmailAsync(
    user.Id, // הוסף את הפרמטר החסר (למשל userId או מה שדורש הממשק החדש)
    user.Email,
    "רישום למערכת הצלת מזון",
    "שלום רב, בקשת ההצטרפות שלך למערכת התקבלה בהצלחה וממתינה לאישור מנהל.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "שליחת מייל אישור הרשמה למתנדב {UserId} נכשלה.", userId);
                }
            }

            var createdDto = _mapper.Map<VolunteerResponseDto>(volunteerEntity);
            return Ok(createdDto);
        }

        [HttpGet("{id}/availability")]
        public async Task<ActionResult<IEnumerable<AvailabilitySlotResponseDto>>> GetVolunteerAvailability(int id)
        {
            var targetId = await GetVolunteerIdFromUserAsync(id);
            if (targetId == null) return Forbid();

            var volunteer = await _volunteerRepository.GetByIdAsync(targetId.Value);
            if (volunteer == null) return NotFound(new { message = "המתנדב לא נמצא." });

            if (!User.IsInRole("Admin"))
            {
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (currentUserIdClaim == null || volunteer.UserId.ToString() != currentUserIdClaim)
                {
                    return Forbid();
                }
            }

            // אין צורך ב-Include! ה-ProjectTo מטפל בחיבור הטבלאות אוטומטית.
            var dtos = await _context.AvailabilitySlots
                .Where(s => s.VolunteerId == targetId.Value)
                .ProjectTo<AvailabilitySlotResponseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return Ok(dtos);
        }
        [HttpPost("{id}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectVolunteer(int id, [FromBody] string rejectionReason)
        {
            var volunteer = await _volunteerRepository.GetByIdAsync(id);
            if (volunteer == null) return NotFound(new { message = "המתנדב לא נמצא." });

            var user = await _userRepository.GetByIdAsync(volunteer.UserId);
            if (user == null) return NotFound(new { message = "המשתמש המקושר לא נמצא." });

            volunteer.ApprovalStatus = DAL.Classes.ApprovalStatus.Rejected;
            await _volunteerRepository.UpdateAsync(volunteer);

            var approvalReq = await _context.ApprovalRequests
                .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RequestedRole == DAL.Classes.RequestedRole.Volunteer);
            if (approvalReq != null)
            {
                approvalReq.Status = DAL.Classes.ApprovalStatus.Rejected;
                approvalReq.RejectionReason = rejectionReason;
                approvalReq.HandledAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrEmpty(user.Email))
            {
                try
                {
                    await _emailService.SendEmailAsync(
    user.Id,
    user.Email,
    "עדכון לגבי בקשת ההצטרפות שלך למערכת",
    $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>לצערנו בקשת ההצטרפות שלך למערכת נדחתה.<br/><b>סיבה:</b> {System.Net.WebUtility.HtmlEncode(rejectionReason)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "שליחת מייל דחייה למתנדב {UserId} נכשלה.", user.Id);
                }
            }

            return Ok(new { message = "הבקשה נדחתה בהצלחה." });
        }

        [HttpPost("{id}/availability")]
        public async Task<IActionResult> SaveVolunteerAvailability(int id, [FromBody] List<CreateAvailabilitySlotDto> slotsDtos)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (slotsDtos == null) return BadRequest(new { message = "נתוני הזמינות ריקים." });

            var targetId = await GetVolunteerIdFromUserAsync(id);
            if (targetId == null) return Forbid();

            var volunteer = await _volunteerRepository.GetByIdAsync(targetId.Value);
            if (volunteer == null) return NotFound(new { message = "המתנדב לא נמצא." });

            if (!User.IsInRole("Admin"))
            {
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (currentUserIdClaim == null || volunteer.UserId.ToString() != currentUserIdClaim)
                {
                    return Forbid();
                }
            }

            var existingSlots = await _context.AvailabilitySlots
                .Where(s => s.VolunteerId == targetId.Value)
                .ToListAsync();

            _context.AvailabilitySlots.RemoveRange(existingSlots);

            foreach (var dto in slotsDtos)
            {
                if (!TimeSpan.TryParse(dto.StartTime, out TimeSpan startTime))
                {
                    if (DateTime.TryParse(dto.StartTime, out DateTime dtStart))
                    {
                        startTime = dtStart.TimeOfDay;
                    }
                    else
                    {
                        return BadRequest(new { message = $"פורמט שעת התחלה אינו תקין: {dto.StartTime}" });
                    }
                }

                if (!TimeSpan.TryParse(dto.EndTime, out TimeSpan endTime))
                {
                    if (DateTime.TryParse(dto.EndTime, out DateTime dtEnd))
                    {
                        endTime = dtEnd.TimeOfDay;
                    }
                    else
                    {
                        return BadRequest(new { message = $"פורמט שעת סיום אינו תקין: {dto.EndTime}" });
                    }
                }

                var slot = new AvailabilitySlot
                {
                    VolunteerId = targetId.Value,
                    RegionId = dto.RegionId,
                    ToRegionId = dto.ToRegionId > 0 ? dto.ToRegionId : (int?)null,
                    Day = (DayOfWeek)dto.Day,
                    StartTime = startTime,
                    EndTime = endTime
                };
                _context.AvailabilitySlots.Add(slot);
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "הזמינות נשמרה בהצלחה." });
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterVolunteerWithUser([FromBody] RegisterVolunteerModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone))
                return BadRequest(new { message = "יש למלא שם, אימייל וטלפון." });

            var existingUser = await _userRepository.GetByEmailAsync(model.Email);

            if (existingUser != null)
                return Conflict(new { message = "כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו." });

            string tempPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(8))
                .Replace('+', 'A').Replace('/', 'B').TrimEnd('=') + "1!";

            var user = new User
            {
                Name = model.Name,
                Email = model.Email,
                Phone = model.Phone,
                PasswordHash = PasswordHasher.Hash(tempPassword),
                Role = UserRole.Volunteer,
                IsEmailVerified = false,
                IsPhoneVerified = false
            };

            await _userRepository.AddAsync(user);

            var volunteerEntity = new Volunteer
            {
                UserId = user.Id,
                VehicleType = model.VehicleType,
                MaxCapacityKg = (double)model.MaxCapacityKg,
                IdNumber = model.IdNumber,
                DriverLicenseNumber = model.DriverLicenseNumber,
                DateOfBirth = model.DateOfBirth,
                ApprovalStatus = DAL.Classes.ApprovalStatus.Pending,
            };

            await _volunteerRepository.AddAsync(volunteerEntity);

            var approvalRequest = new ApprovalRequest
            {
                UserId = user.Id,
                RequestedRole = DAL.Classes.RequestedRole.Volunteer,
                Status = DAL.Classes.ApprovalStatus.Pending,
                RejectionReason = string.Empty,
                ValidationDetails = string.Empty
            };
            _context.ApprovalRequests.Add(approvalRequest);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailAsync(
     user.Id,
     user.Email,
     "פרטי ההתחברות שלך למערכת הצלת מזון",
     $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות שלך למערכת התקבלה בהצלחה.<br/>הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>מומלץ לשנות את הסיסמה לאחר ההתחברות הראשונה.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "שליחת מייל סיסמה זמנית למתנדב {UserId} נכשלה.", user.Id);
            }

            return Ok(new { message = "הרישום בוצע בהצלחה! סיסמה זמנית נשלחה לכתובת האימייל שלך." });
        }
    }
}