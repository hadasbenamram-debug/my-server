using AutoMapper;
using BLL.Interfaces;
using BLL.Integrations.GoogleMaps;
using BLL.Algorithms;
using DAL.Classes;
using DAL;
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
    public class SuppliersController : ControllerBase
    {
        private readonly ISupplierRepository _supplierRepository;
        private readonly IUserRepository _userRepository;
        private readonly IGoogleMapsDistanceService _googleMapsService;
        private readonly FoodRescueDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<SuppliersController> _logger;

        public SuppliersController(
            ISupplierRepository supplierRepository,
            IUserRepository userRepository,
            IGoogleMapsDistanceService googleMapsService,
            FoodRescueDbContext context,
            IEmailService emailService,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<SuppliersController> logger)
        {
            _supplierRepository = supplierRepository;
            _userRepository = userRepository;
            _googleMapsService = googleMapsService;
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
        }

        public class RegisterSupplierModel
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string BusinessName { get; set; }
            public string Address { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string TaxId { get; set; }
            public string BusinessLicenseFilePath { get; set; }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<SupplierResponseDto>>> GetSuppliers()
        {
            var suppliers = await _supplierRepository.GetAllAsync();
            var supplierDtos = _mapper.Map<IEnumerable<SupplierResponseDto>>(suppliers);
            return Ok(supplierDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SupplierResponseDto>> GetSupplier(int id)
        {
            // תיקון: חיפוש כפול המונע שגיאת 404 במקרה שמועבר User ID במקום Supplier ID
            var s = await _supplierRepository.GetByIdAsync(id) ?? await _supplierRepository.GetByUserIdAsync(id);
            if (s == null) return NotFound();

            if (!User.IsInRole("Admin"))
            {
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (currentUserIdClaim == null || s.UserId.ToString() != currentUserIdClaim)
                {
                    return Forbid();
                }
            }

            var supplierDto = _mapper.Map<SupplierResponseDto>(s);
            return Ok(supplierDto);
        }

        [HttpPost]
        public async Task<ActionResult<SupplierResponseDto>> CreateSupplier([FromForm] CreateSupplierDto supplierDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return Unauthorized(new { message = "המשתמש המחובר לא נמצא." });

            var existing = await _supplierRepository.GetByUserIdAsync(userId);
            if (existing != null)
                return Conflict(new { message = "למשתמש זה כבר קיים פרופיל ספק." });

            // טיפול ושמירת קובץ הרישיון בתיקיית Files ייעודית
            string relativePath = string.Empty;
            if (supplierDto.LicenseFile != null && supplierDto.LicenseFile.Length > 0)
            {
                // בדיקת סיומות מותרות (לדוגמה: PDF ותמונות בלבד)
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
                string extension = Path.GetExtension(supplierDto.LicenseFile.FileName).ToLowerInvariant();

                if (Array.Exists(allowedExtensions, ext => ext == extension) == false)
                {
                    return BadRequest(new { message = "סיומת קובץ לא חוקית. יש להעלות קובץ PDF או תמונה בלבד." });
                }

                // הגדרת תיקיית Files תחת wwwroot
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Files", "licenses");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // יצירת שם ייחודי לקובץ למניעת התנגשויות
                string uniqueFileName = Guid.NewGuid().ToString() + extension;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await supplierDto.LicenseFile.CopyToAsync(fileStream);
                }

                // שמירת הנתיב היחסי כמחרוזת (STRING) שתישמר במסד הנתונים
                relativePath = "/Files/licenses/" + uniqueFileName;
            }
            else
            {
                return BadRequest(new { message = "חובה לצרף קובץ רישיון עסק." });
            }

            var supplierEntity = _mapper.Map<Supplier>(supplierDto);

            supplierEntity.UserId = userId;
            supplierEntity.BusinessLicenseFilePath = relativePath; // השמת הנתיב הטקסטואלי במסד הנתונים
            supplierEntity.ApprovalStatus = DAL.Classes.ApprovalStatus.Pending;
            supplierEntity.GooglePlaceId = "N/A";

            await _supplierRepository.AddAsync(supplierEntity);

            var approvalRequest = new ApprovalRequest
            {
                UserId = userId,
                RequestedRole = DAL.Classes.RequestedRole.Supplier,
                Status = DAL.Classes.ApprovalStatus.Pending,
                RejectionReason = string.Empty,
                ValidationDetails = string.Empty,
                CreatedAt = DateTime.UtcNow
            };
            _context.ApprovalRequests.Add(approvalRequest);
            await _context.SaveChangesAsync();

            var createdDto = _mapper.Map<SupplierResponseDto>(supplierEntity);
            return Ok(createdDto);
        }
        [HttpPost("{id}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectSupplier(int id, [FromBody] string reason)
        {
            var supplier = await _supplierRepository.GetByIdAsync(id) ?? await _supplierRepository.GetByUserIdAsync(id);
            if (supplier == null) return NotFound(new { message = "הספק לא נמצא" });

            supplier.ApprovalStatus = DAL.Classes.ApprovalStatus.Rejected;
            await _supplierRepository.UpdateAsync(supplier);

            var approvalRequest = await _context.ApprovalRequests
                .FirstOrDefaultAsync(ar => ar.UserId == supplier.UserId && ar.RequestedRole == DAL.Classes.RequestedRole.Supplier);
            if (approvalRequest != null)
            {
                approvalRequest.Status = DAL.Classes.ApprovalStatus.Rejected;
                approvalRequest.RejectionReason = reason;
                approvalRequest.HandledAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "הבקשה נדחתה בהצלחה." });
        }
        [HttpGet("current")]
        public async Task<ActionResult<SupplierResponseDto>> GetCurrentSupplier()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var supplier = await _supplierRepository.GetByUserIdAsync(userId);
            if (supplier == null)
                return NotFound(new { message = "לא נמצא פרופיל ספק המשוייך למשתמש המחובר." });

            var supplierDto = _mapper.Map<SupplierResponseDto>(supplier);
            return Ok(supplierDto);
        }
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterSupplierWithUser([FromBody] RegisterSupplierModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone))
                return BadRequest(new { message = "יש למלא שם, אימייל וטלפון." });

            var existingUser = await _userRepository.GetByEmailAsync(model.Email);
            User user;
            string tempPassword = null;

            if (existingUser != null)
            {
                user = existingUser;

                var existingSupplier = await _supplierRepository.GetByUserIdAsync(user.Id);
                if (existingSupplier != null)
                {
                    return Conflict(new { message = "למשתמש זה כבר קיים פרופיל ספק פעיל במערכת." });
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
                    Role = UserRole.Supplier,
                    IsEmailVerified = false,
                    IsPhoneVerified = false
                };

                await _userRepository.AddAsync(user);
                await _context.SaveChangesAsync();
            }

            var supplierEntity = new Supplier
            {
                UserId = user.Id,
                BusinessName = model.BusinessName,
                Address = model.Address,
                Latitude = model.Latitude != 0 ? model.Latitude : 32.0853,
                Longitude = model.Longitude != 0 ? model.Longitude : 34.7818,
                TaxId = model.TaxId,
                BusinessLicenseFilePath = model.BusinessLicenseFilePath,
                GooglePlaceId = "N/A",
                ApprovalStatus = DAL.Classes.ApprovalStatus.Pending
            };

            await _supplierRepository.AddAsync(supplierEntity);

            var approvalRequest = new ApprovalRequest
            {
                UserId = user.Id,
                RequestedRole = DAL.Classes.RequestedRole.Supplier,
                Status = DAL.Classes.ApprovalStatus.Pending,
                RejectionReason = string.Empty,
                ValidationDetails = string.Empty,
                CreatedAt = DateTime.UtcNow
            };
            _context.ApprovalRequests.Add(approvalRequest);
            await _context.SaveChangesAsync();

            if (tempPassword != null)
            {
                try
                {
                    await _emailService.SendEmailAsync(
                        user.Id,
                        user.Email,
                        "פרטי ההתחברות שלך למערכת הצלת מזון",
                        $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות שלך כספק למערכת התקבלה בהצלחה.<br/>הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>מומלץ לשנות את הסיסמה לאחר ההתחברות הראשונה.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "שליחת מייל סיסמה זמנית לספק {UserId} נכשלה.", user.Id);
                }
            }

            var responseDto = _mapper.Map<SupplierResponseDto>(supplierEntity);
            return Ok(new { message = "הרישום בוצע בהצלחה!", data = responseDto });
        }

        [HttpPost("{id}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveSupplier(int id)
        {
            var supplier = await _supplierRepository.GetByIdAsync(id);
            if (supplier == null)
            {
                supplier = await _supplierRepository.GetByUserIdAsync(id);
            }

            if (supplier == null)
            {
                return NotFound(new { message = "הספק לא נמצא" });
            }

            var user = await _userRepository.GetByIdAsync(supplier.UserId);
            if (user == null)
            {
                return NotFound(new { message = "המשתמש המקושר לא נמצא" });
            }

            supplier.ApprovalStatus = DAL.Classes.ApprovalStatus.Approved;
            await _supplierRepository.UpdateAsync(supplier);

            var approvalRequest = await _context.ApprovalRequests
                .FirstOrDefaultAsync(ar => ar.UserId == supplier.UserId && ar.RequestedRole == DAL.Classes.RequestedRole.Supplier);

            if (approvalRequest != null)
            {
                approvalRequest.Status = DAL.Classes.ApprovalStatus.Approved;
                approvalRequest.HandledAt = DateTime.UtcNow;
                _context.ApprovalRequests.Update(approvalRequest);
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
            string frontendAvailabilityUrl = $"http://localhost:5174/app/supplier/availability?token={userToken}";

            if (!string.IsNullOrEmpty(user.Email))
            {
                try
                {
                    string emailSubject = "אישור חשבון ספק ומילוי זמינות שבועית";
                    string emailBody = $@"
                        שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/><br/>
                        שמחים לעדכן כי חשבון הספק שלך במערכת <b>אושר על ידי המנהל!</b><br/>
                        כדי שנוכל להתאים לך תרומות מזון, עליך להגדיר את זמינות העסק.<br/><br/>
                        לחץ על הקישור הבא כדי להיכנס למערכת ולהגדיר את נתוני הזמינות:<br/>
                        <a href='{frontendAvailabilityUrl}' target='_blank'>מעבר להגדרת זמינות</a><br/><br/>
                        תודה רבה!
                    ";

                    await _emailService.SendEmailAsync(user.Id, user.Email, emailSubject, emailBody);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "שליחת מייל אישור ספק ל-{Email} נכשלה.", user.Email);
                }
            }

            return Ok(new { message = "הספק אושר בהצלחה ומייל נשלח אליו." });
        }
    }
}