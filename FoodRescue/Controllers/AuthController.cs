using BLL.Algorithms;
using BLL.Interfaces;
using DAL.Classes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IConfiguration configuration,
            IUserRepository userRepository,
            INgoRepository ngoRepository,
            ISupplierRepository supplierRepository,
            IMemoryCache memoryCache,
            IEmailService emailService,
            ILogger<AuthController> logger)
        {
            _configuration = configuration;
            _userRepository = userRepository;
            _ngoRepository = ngoRepository;
            _supplierRepository = supplierRepository;
            _memoryCache = memoryCache;
            _emailService = emailService;
            _logger = logger;
        }

        public class LoginModel
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public class RegisterModel
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string Password { get; set; }
        }

        public class SendVerificationDto
        {
            public string Email { get; set; }
        }

        public class VerifyCodeDto
        {
            public string Email { get; set; }
            public string Code { get; set; }
        }

        public class ForgotPasswordModel
        {
            public string Email { get; set; }
        }

        public class ResetPasswordModel
        {
            public string Token { get; set; }
            public string NewPassword { get; set; }
        }

        // רישום משתמש חדש במערכת
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            if (model == null)
            {
                return BadRequest(new { message = "יש למלא את כל שדות החובה." });
            }

            if (string.IsNullOrEmpty(model.Password) || model.Password.Length < 8)
            {
                return BadRequest(new { message = "הסיסמה חייבת להכיל לפחות 8 תווים." });
            }

            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone))
            {
                return BadRequest(new { message = "יש למלא שם, אימייל וטלפון." });
            }
           var existing = await _userRepository.GetByEmailAsync(model.Email);
            if (existing != null)
            {
               return Conflict(new { message = "כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו." });
          }

            var user = new User
            {
                Name = model.Name,
                Email = model.Email,
                Phone = model.Phone,
                PasswordHash = PasswordHasher.Hash(model.Password),
                Role = UserRole.User,
                IsEmailVerified = false,
                IsPhoneVerified = false
            };

            await _userRepository.AddAsync(user);

            var token = GenerateJwtToken(user.Id, user.Email, user.Role.ToString());
            return Ok(new { Token = token, Role = user.Role.ToString() });
        }

        // התחברות למערכת עם בדיקת סטטוס אישור לעמותות וספקים
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
            {
                return Unauthorized(new { message = "שם משתמש או סיסמה שגויים." });
            }

            string emailKey = model.Email.Trim().ToLower();
            string lockoutKey = $"lockout_{emailKey}";
            string attemptsKey = $"attempts_{emailKey}";

            if (_memoryCache.TryGetValue(lockoutKey, out DateTime lockoutEnd) && lockoutEnd > DateTime.UtcNow)
            {
                var remainingMinutes = Math.Ceiling((lockoutEnd - DateTime.UtcNow).TotalMinutes);
                return StatusCode(423, new { message = $"החשבון נעול זמנית עקב ריבוי ניסיונות כושלים. נסה שוב עוד {remainingMinutes} דקות." });
            }

            var user = await _userRepository.GetByEmailAsync(model.Email);

            bool passwordOk = user != null
                ? PasswordHasher.Verify(model.Password, user.PasswordHash)
                : PasswordHasher.Verify(model.Password, PasswordHasher.Hash(Guid.NewGuid().ToString()));

            if (user == null || !passwordOk)
            {
                int attempts = _memoryCache.GetOrCreate(attemptsKey, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                    return 0;
                });

                attempts++;
                _memoryCache.Set(attemptsKey, attempts, TimeSpan.FromMinutes(30));

                if (attempts >= 5)
                {
                    _memoryCache.Set(lockoutKey, DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                    _memoryCache.Remove(attemptsKey);
                }

                return Unauthorized(new { message = "שם משתמש או סיסמה שגויים." });
            }

            // --- בדיקת סטטוס אישור עבור עמותות וספקים ---
            var ngo = await _ngoRepository.GetByUserIdAsync(user.Id);
            if (ngo != null)
            {
                if (ngo.ApprovalStatus == ApprovalStatus.Pending)
                {
                    return StatusCode(403, new { message = "חשבון העמותה שלך ממתין לאישור מנהל מערכת." });
                }
                if (ngo.ApprovalStatus == ApprovalStatus.Rejected)
                {
                    return StatusCode(403, new { message = "בקשת הצטרפות העמותה שלך נדחתה." });
                }
            }

            var supplier = await _supplierRepository.GetByUserIdAsync(user.Id);
            if (supplier != null)
            {
                if (supplier.ApprovalStatus == ApprovalStatus.Pending)
                {
                    return StatusCode(403, new { message = "חשבון הספק שלך ממתין לאישור מנהל מערכת." });
                }
                if (supplier.ApprovalStatus == ApprovalStatus.Rejected)
                {
                    return StatusCode(403, new { message = "בקשת הצטרפות הספק שלך נדחתה." });
                }
            }
            // ------------------------------------------

            _memoryCache.Remove(attemptsKey);
            _memoryCache.Remove(lockoutKey);

            var token = GenerateJwtToken(user.Id, user.Email, user.Role.ToString());
            return Ok(new { Token = token, Role = user.Role.ToString() });
        }

        private const int MaxVerificationAttempts = 5;

        [HttpPost("send-email-verification")]
        [Authorize]
        public async Task<IActionResult> SendEmailVerification()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            if (user.IsEmailVerified)
                return BadRequest(new { message = "כתובת האימייל כבר מאומתת." });

            string cooldownKey = $"email_verification_cooldown_{user.Id}";
            if (_memoryCache.TryGetValue(cooldownKey, out _))
                return StatusCode(429, new { message = "נשלח קוד לאחרונה. נסה שוב בעוד דקה." });

            string verificationCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            string codeKey = $"email_verification_{user.Id}";
            string attemptsKey = $"email_verification_attempts_{user.Id}";

            try
            {
                await _emailService.SendEmailAsync(
                    user.Id, // הוספת מזהה המשתמש כפרמטר ראשון
                    user.Email,
                    "קוד אימות - לתת באהבה",
                    $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>קוד האימות שלך הוא: <b>{verificationCode}</b><br/>הקוד תקף ל-15 דקות.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "שליחת מייל אימות למשתמש {UserId} נכשלה.", user.Id);
                return StatusCode(503, new { message = "לא ניתן לשלוח מייל כרגע. נסה שוב מאוחר יותר." });
            }

            _memoryCache.Set(codeKey, verificationCode, TimeSpan.FromMinutes(15));
            _memoryCache.Remove(attemptsKey);
            _memoryCache.Set(cooldownKey, true, TimeSpan.FromMinutes(1));

            return Ok(new { message = "קוד האימות נשלח לכתובת המייל שלך." });
        }

        [HttpPost("verify-email")]
        [Authorize]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyCodeDto model)
        {
            if (string.IsNullOrWhiteSpace(model?.Code))
                return BadRequest(new { message = "יש לספק קוד אימות." });

            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            string codeKey = $"email_verification_{user.Id}";
            string attemptsKey = $"email_verification_attempts_{user.Id}";

            if (!_memoryCache.TryGetValue(codeKey, out string savedCode))
                return BadRequest(new { message = "קוד האימות שגוי או שפג תוקפו." });

            if (savedCode != model.Code.Trim())
            {
                int attempts = _memoryCache.TryGetValue(attemptsKey, out int current) ? current + 1 : 1;
                _memoryCache.Set(attemptsKey, attempts, TimeSpan.FromMinutes(15));
                if (attempts >= MaxVerificationAttempts)
                {
                    _memoryCache.Remove(codeKey);
                    _memoryCache.Remove(attemptsKey);
                    return BadRequest(new { message = "בוצעו יותר מדי ניסיונות שגויים. יש לבקש קוד חדש." });
                }
                return BadRequest(new { message = "קוד האימות שגוי או שפג תוקפו." });
            }

            user.IsEmailVerified = true;
            await _userRepository.UpdateAsync(user);

            _memoryCache.Remove(codeKey);
            _memoryCache.Remove(attemptsKey);

            return Ok(new { message = "כתובת האימייל אומתה בהצלחה!" });
        }

        private static string HashResetToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordModel model)
        {
            const string genericMessage = "אם כתובת האימייל קיימת במערכת, נשלח אליה קישור לאיפוס סיסמה.";

            if (string.IsNullOrWhiteSpace(model?.Email))
                return BadRequest(new { message = "יש לספק כתובת אימייל." });

            var user = await _userRepository.GetByEmailAsync(model.Email);
            if (user == null)
                return Ok(new { message = genericMessage });

            string cooldownKey = $"password_reset_cooldown_{user.Id}";
            if (_memoryCache.TryGetValue(cooldownKey, out _))
                return Ok(new { message = genericMessage });

            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            string tokenKey = $"password_reset_{HashResetToken(token)}";
            string userTokenKey = $"password_reset_user_{user.Id}";

            var baseUrl = _configuration["App:FrontendBaseUrl"];
            string linkHtml = string.IsNullOrWhiteSpace(baseUrl)
                ? ""
                : $"<br/><a href=\"{baseUrl.TrimEnd('/')}/reset-password?token={token}\">לחץ כאן לאיפוס הסיסמה</a>";

            try
            {
                
                    await _emailService.SendEmailAsync(
                        user.Id, // הוספת מזהה המשתמש כפרמטר ראשון
                        user.Email,
                        "איפוס סיסמה - לתת באהבה",
                        $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)},<br/>התקבלה בקשה לאיפוס הסיסמה שלך.<br/>קוד האיפוס: <b>{token}</b><br/>הקוד תקף ל-30 דקות. אם לא ביקשת איפוס, אפשר להתעלם מהודעה זו.{linkHtml}");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "שליחת מייל איפוס סיסמה למשתמש {UserId} נכשלה.", user.Id);
                return Ok(new { message = genericMessage });
            }

            if (_memoryCache.TryGetValue(userTokenKey, out string previousKey))
                _memoryCache.Remove(previousKey);

            _memoryCache.Set(tokenKey, user.Id, TimeSpan.FromMinutes(30));
            _memoryCache.Set(userTokenKey, tokenKey, TimeSpan.FromMinutes(30));
            _memoryCache.Set(cooldownKey, true, TimeSpan.FromMinutes(1));

            return Ok(new { message = genericMessage });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.Token) || string.IsNullOrEmpty(model.NewPassword))
                return BadRequest(new { message = "יש לספק טוקן וסיסמה חדשה." });

            if (model.NewPassword.Length < 8)
                return BadRequest(new { message = "הסיסמה חייבת להכיל לפחות 8 תווים." });

            string tokenKey = $"password_reset_{HashResetToken(model.Token.Trim())}";
            if (!_memoryCache.TryGetValue(tokenKey, out int userId))
                return BadRequest(new { message = "הטוקן שגוי או שפג תוקפו." });

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return BadRequest(new { message = "הטוקן שגוי או שפג תוקפו." });

            user.PasswordHash = PasswordHasher.Hash(model.NewPassword);
            await _userRepository.UpdateAsync(user);

            _memoryCache.Remove(tokenKey);
            _memoryCache.Remove($"password_reset_user_{userId}");
            string emailKey = user.Email.Trim().ToLower();
            _memoryCache.Remove($"lockout_{emailKey}");
            _memoryCache.Remove($"attempts_{emailKey}");

            return Ok(new { message = "הסיסמה אופסה בהצלחה. אפשר להתחבר עם הסיסמה החדשה." });
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return null;
            return await _userRepository.GetByIdAsync(userId);
        }

        private string GenerateJwtToken(int userId, string email, string role)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKeyString = jwtSettings["Secret"]
                                  ?? throw new InvalidOperationException("JWT Secret Key is missing in configuration.");

            var secretKey = Encoding.UTF8.GetBytes(secretKeyString);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, email),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(secretKey);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(3),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}