using AutoMapper;
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
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class NgosController : ControllerBase
    {
        private readonly INgoRepository _ngoRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRegionRepository _regionRepository;
        private readonly FoodRescueDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<NgosController> _logger;

        public NgosController(
            INgoRepository ngoRepository,
            IUserRepository userRepository,
            IRegionRepository regionRepository,
            FoodRescueDbContext context,
            IEmailService emailService,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<NgosController> logger)
        {
            _ngoRepository = ngoRepository;
            _userRepository = userRepository;
            _regionRepository = regionRepository;
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
        }

        public class RegisterNgoModel
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string NgoName { get; set; }
            public int DailyCapacity { get; set; }
            public string Address { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string RegistrationNumber { get; set; }
            public int RegionId { get; set; }
            public bool ReceivesOnFriday { get; set; }
            public TimeSpan? FridayOpenTime { get; set; }
            public TimeSpan? FridayCloseTime { get; set; }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<NgoResponseDto>>> GetNgos()
        {
            var ngos = await _ngoRepository.GetAllAsync();
            var ngoDtos = _mapper.Map<IEnumerable<NgoResponseDto>>(ngos);
            return Ok(ngoDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<NgoResponseDto>> GetNgo(int id)
        {
            var n = await _ngoRepository.GetByIdAsync(id);
            if (n == null) return NotFound();

            var ngoDto = _mapper.Map<NgoResponseDto>(n);
            return Ok(ngoDto);
        }
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterNgoWithUser([FromBody] RegisterNgoModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone))
                return BadRequest(new { message = "יש למלא שם, אימייל וטלפון." });

            if (await _regionRepository.GetByIdAsync(model.RegionId) == null)
                return BadRequest(new { message = "האזור שנבחר אינו קיים." });

            var existingUser = await _userRepository.GetByEmailAsync(model.Email);
            User user;
            string tempPassword = null;

            if (existingUser != null)
            {
                user = existingUser;
                var existingNgo = await _ngoRepository.GetByUserIdAsync(user.Id);
                if (existingNgo != null)
                {
                    return Conflict(new { message = "למשתמש זה כבר קיים פרופיל עמותה פעיל במערכת." });
                }
            }
            else
            {
                tempPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(8))
                    .Replace('+', 'A').Replace('/', 'B').TrimEnd('=') + "1!";

                user = new User
                {
                    Name = model.Name,
                    Email = model.Email,
                    Phone = model.Phone,
                    PasswordHash = PasswordHasher.Hash(tempPassword),
                    Role = UserRole.User,
                    IsEmailVerified = false,
                    IsPhoneVerified = false
                };

                // שמירה אחידה דרך ה-Context או וידוא שהריפוזיטורי מבצע SaveChanges
                await _userRepository.AddAsync(user);

                // אם הריפוזיטורי אינו שומר לבד, נוודא שהקונטקסט שומר כעת כדי לקבל user.Id אמיתי
                await _context.SaveChangesAsync();
            }

            // הגנה קשיחה למניעת ערך 0 או שליחה עם ID שגוי
            if (user.Id <= 0)
            {
                return BadRequest(new { message = "אירעה שגיאה ביצירת המזהה עבור המשתמש." });
            }

            var ngoEntity = new Ngo
            {
                UserId = user.Id,
                NgoName = model.NgoName,
                DailyCapacity = model.DailyCapacity,
                Address = model.Address,
                Latitude = model.Latitude != 0 ? model.Latitude : 32.0853,
                Longitude = model.Longitude != 0 ? model.Longitude : 34.7818,
                RegistrationNumber = model.RegistrationNumber,
                RegionId = model.RegionId,
                ReceivesOnFriday = model.ReceivesOnFriday,
                FridayOpenTime = model.FridayOpenTime,
                FridayCloseTime = model.FridayCloseTime,
                GooglePlaceId = "N/A",
                ApprovalStatus = DAL.Classes.ApprovalStatus.Pending
            };

            await _ngoRepository.AddAsync(ngoEntity);

            var approvalRequest = new ApprovalRequest
            {
                UserId = user.Id,
                RequestedRole = DAL.Classes.RequestedRole.Ngo,
                Status = DAL.Classes.ApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                RejectionReason = string.Empty,
                ValidationDetails = string.Empty
            };

            _context.ApprovalRequests.Add(approvalRequest);

            // שמירה סופית של כל השינויים (עמותה ובקשת אישור)
            await _context.SaveChangesAsync();

            if (tempPassword != null)
            {
                try
                {
                    await _emailService.SendEmailAsync(
                        user.Id,
                        user.Email,
                        "פרטי ההתחברות שלך למערכת הצלת מזון",
                        $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות של העמותה שלך התקבלה בהצלחה.<br/>הסיסמה הזמנית שלך היא: <b>{tempPassword}</b>");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "שליחת מייל סיסמה זמנית לעמותה נכשלה.");
                }
            }

            var responseDto = _mapper.Map<NgoResponseDto>(ngoEntity);
            return Ok(new { message = "הרישום בוצע בהצלחה!", data = responseDto });
        }

        [HttpPost("{id}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveNgo(int id)
        {
            var ngo = await _ngoRepository.GetByIdAsync(id);
            if (ngo == null) return NotFound(new { message = "העמותה לא נמצאה." });

            var user = await _userRepository.GetByIdAsync(ngo.UserId);
            if (user == null) return NotFound(new { message = "המשתמש המקושר לא נמצא." });

            ngo.ApprovalStatus = DAL.Classes.ApprovalStatus.Approved;
            await _ngoRepository.UpdateAsync(ngo);

            var approvalReq = await _context.ApprovalRequests
                .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RequestedRole == DAL.Classes.RequestedRole.Ngo);
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

            string frontendNgoHoursUrl = $"http://localhost:5174/app/ngo/hours?token={userToken}";

            string emailSubject = "אישור הצטרפות העמותה למערכת והגדרת זמינות";
            string emailBody = $@"
                שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/><br/>
                שמחים לעדכן כי בקשת ההצטרפות של העמותה שלך למערכת <b>אושרה על ידי המנהל!</b><br/>
                כדי שנוכל להתאים לך תרומות מזון, עליך להגדיר את כתובת העמותה ושעות הקבלה.<br/><br/>
                לחץ על הקישור הבא כדי להיכנס למערכת ולהגדיר את נתוני הזמינות:<br/>
                <a href='{frontendNgoHoursUrl}' target='_blank'>מעבר להגדרת מיקום ושעות קבלה</a><br/><br/>
                תודה רבה!
            ";

            try
            {
                // הועבר user.Id כפרמטר הראשון
                await _emailService.SendEmailAsync(user.Id, user.Email, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "שליחת מייל אישור לעמותה {UserId} נכשלה.", user.Id);
            }

            return Ok(new { message = "העמותה אושרה בהצלחה ונשלח מייל עם טוקן זמינות למשתמש!" });
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectNgo(int id, [FromBody] string reason)
        {
            var ngo = await _ngoRepository.GetByIdAsync(id);
            if (ngo == null) return NotFound(new { message = "העמותה לא נמצאה." });

            ngo.ApprovalStatus = DAL.Classes.ApprovalStatus.Rejected;
            await _ngoRepository.UpdateAsync(ngo);

            return Ok(new { message = "העמותה נדחתה בהצלחה." });
        }

        [HttpPut("my-hours")]
        public async Task<IActionResult> UpdateMyNgoHours([FromBody] UpdateNgoHoursDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
                return Unauthorized(new { message = "משתמש לא מזוהה." });

            var ngo = await _ngoRepository.GetByUserIdAsync(userId);
            if (ngo == null) return NotFound(new { message = "העמותה לא נמצאה למשתמש זה." });

            ngo.Address = dto.Address;
            ngo.Latitude = dto.Latitude;
            ngo.Longitude = dto.Longitude;
            ngo.SunToThuOpenTime = dto.SunToThuOpenTime ?? TimeSpan.Zero;
            ngo.SunToThuCloseTime = dto.SunToThuCloseTime ?? TimeSpan.Zero;
            ngo.ReceivesOnFriday = dto.ReceivesOnFriday;
            ngo.FridayOpenTime = dto.FridayOpenTime;
            ngo.FridayCloseTime = dto.FridayCloseTime;

            await _ngoRepository.UpdateAsync(ngo);

            return Ok(new { message = "שעות הקבלה והמיקום עודכנו בהצלחה." });
        }
    }
}