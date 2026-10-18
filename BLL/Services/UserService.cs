using Microsoft.AspNetCore.Identity;
using AutoMapper;
using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace BLL.Services
{
    public sealed class UserService : IUserService
    {
        private const string InvalidCredentialsMessage = "שם משתמש או סיסמה שגויים.";
        private const int MaxVerificationAttempts = 5;
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromMinutes(15);

        private readonly IUserRepository _userRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly ILoginLockoutService _lockout;
        private readonly IActionTokenService _actionTokens;
        private readonly INotificationService _notifications;
        private readonly ITokenService _tokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserService> _logger;
        private readonly IMapper _mapper;
        private readonly IPasswordHasher<User> _passwordHasher;

        // hash קבוע לבדיקה מול מייל שלא קיים, כדי שזמן התגובה יהיה דומה (לא חושף אילו מיילים רשומים)
        private static readonly Lazy<string> DummyHash =
            new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString()));

        public UserService(
            IUserRepository userRepository,
            INgoRepository ngoRepository,
            ISupplierRepository supplierRepository,
            ILoginLockoutService lockout,
            IActionTokenService actionTokens,
            INotificationService notifications,
            ITokenService tokenService,
            IConfiguration configuration,
            ILogger<UserService> logger,
            IMapper mapper,
            IPasswordHasher<User> passwordHasher)
        {
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _ngoRepository = ngoRepository;
            _supplierRepository = supplierRepository;
            _lockout = lockout;
            _actionTokens = actionTokens;
            _notifications = notifications;
            _tokenService = tokenService;
            _configuration = configuration;
            _logger = logger;
            _mapper = mapper;
        }

        // =====================================================
        // Register
        // =====================================================

        public async Task<AuthResult> RegisterAsync(RegisterDto dto)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            if (await _userRepository.GetByEmailAsync(email) != null)
                return EmailTaken();

            var user = new User
            {
                Name = dto.Name.Trim(),
                Email = email,
                Phone = dto.Phone.Trim(),
                Role = UserRole.User,               // never taken from the client
                IsEmailVerified = false,
                IsPhoneVerified = false,
                SecurityStamp = NewStamp()
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

            // Two simultaneous registrations may hit the unique index on Email.
            if (!await _userRepository.TryAddAsync(user))
                return EmailTaken();

            return AuthResult.Success(BuildResponse(user));
        }

        private static AuthResult EmailTaken() =>
            AuthResult.Fail(
                AuthError.EmailTaken,
                "כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

        // =====================================================
        // Login - נעילה לפי (מייל + IP) ולפי IP, נשמרת במסד
        // =====================================================

        public async Task<AuthResult> LoginAsync(LoginDto dto, string? ipAddress = null)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            var remaining = await _lockout.GetLockRemainingAsync(email, ipAddress);
            if (remaining.HasValue)
            {
                var minutes = Math.Max(1, (int)Math.Ceiling(remaining.Value.TotalMinutes));
                return AuthResult.Fail(
                    AuthError.LockedOut,
                    $"בוצעו יותר מדי ניסיונות כושלים. נסה שוב עוד {minutes} דקות.");
            }

            var user = await _userRepository.GetByEmailAsync(email);

            // The dummy hash keeps the response time similar for unknown e-mail addresses.
            var verification = user != null
                ? _passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? string.Empty, dto.Password)
                : _passwordHasher.VerifyHashedPassword(new User(), DummyHash.Value, dto.Password);

            if (user == null || verification == PasswordVerificationResult.Failed)
            {
                await _lockout.RecordAsync(email, ipAddress, user?.Id, success: false,
                    failureReason: user == null ? "UnknownEmail" : "WrongPassword");
                return AuthResult.Fail(AuthError.InvalidCredentials, InvalidCredentialsMessage);
            }

            // סיסמה שנשמרה בפורמט הישן: שומרים אותה מחדש עם ה-PasswordHasher של Microsoft
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
                await _userRepository.UpdateAsync(user);
            }

            // --- approval status for NGOs and suppliers ---
            var ngo = await _ngoRepository.GetByUserIdAsync(user.Id);
            if (ngo != null)
            {
                if (ngo.ApprovalStatus == ApprovalStatus.Pending)
                    return AuthResult.Fail(AuthError.NgoPendingApproval, "חשבון העמותה שלך ממתין לאישור מנהל מערכת.");

                if (ngo.ApprovalStatus == ApprovalStatus.Rejected)
                    return AuthResult.Fail(AuthError.NgoRejected, "בקשת הצטרפות העמותה שלך נדחתה.");
            }

            var supplier = await _supplierRepository.GetByUserIdAsync(user.Id);
            if (supplier != null)
            {
                if (supplier.ApprovalStatus == ApprovalStatus.Pending)
                    return AuthResult.Fail(AuthError.SupplierPendingApproval, "חשבון הספק שלך ממתין לאישור מנהל מערכת.");

                if (supplier.ApprovalStatus == ApprovalStatus.Rejected)
                    return AuthResult.Fail(AuthError.SupplierRejected, "בקשת הצטרפות הספק שלך נדחתה.");
            }

            await _lockout.RecordAsync(email, ipAddress, user.Id, success: true);

            return AuthResult.Success(BuildResponse(user));
        }

        // =====================================================
        // Forgot / reset password - טוקן חד-פעמי במסד (רק ה-Hash נשמר)
        // =====================================================

        public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
        {
            var user = await _userRepository.GetByEmailAsync(EmailNormalizer.Normalize(dto.Email));
            if (user == null)
                return;

            var lastIssued = await _actionTokens.GetLastIssuedAtAsync(user.Id, ActionTokenPurpose.PasswordReset);
            if (lastIssued.HasValue && DateTime.UtcNow - lastIssued.Value < ResendCooldown)
                return;

            // קישור קודם שלא נוצל מבוטל - רק הקישור האחרון עובד
            await _actionTokens.RevokeAsync(user.Id, ActionTokenPurpose.PasswordReset);
            string token = await _actionTokens.CreateLinkTokenAsync(user.Id, ActionTokenPurpose.PasswordReset, ResetTokenLifetime);

            var baseUrl = _configuration["App:FrontendBaseUrl"];
            string linkHtml = string.IsNullOrWhiteSpace(baseUrl)
                ? ""
                : $"<br/><a href=\"{baseUrl.TrimEnd('/')}/app/reset-password?token={WebUtility.UrlEncode(token)}\">לחץ כאן לאיפוס הסיסמה</a>";

            await _notifications.QueueEmailAsync(
                user.Id,
                user.Email,
                "איפוס סיסמה - לתת באהבה",
                $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>התקבלה בקשה לאיפוס הסיסמה שלך.<br/>קוד האיפוס: <b>{token}</b><br/>הקוד תקף ל-30 דקות ולשימוש אחד. אם לא ביקשת איפוס, אפשר להתעלם מהודעה זו.{linkHtml}",
                summary: "נשלח קישור לאיפוס סיסמה");
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto dto)
        {
            var token = await _actionTokens.FindValidLinkTokenAsync(ActionTokenPurpose.PasswordReset, dto.Token);
            if (token?.UserId == null)
                return false;

            var user = await _userRepository.GetByIdAsync(token.UserId.Value);
            if (user == null)
                return false;

            user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;
            user.SecurityStamp = NewStamp();          // כל הטוקנים שהונפקו לפני האיפוס מפסיקים לעבוד
            await _userRepository.UpdateAsync(user);

            await _actionTokens.ConsumeAsync(token);
            await _actionTokens.RevokeAsync(user.Id, ActionTokenPurpose.PasswordReset);

            return true;
        }

        // =====================================================
        // Email / phone verification - קוד בן 6 ספרות במסד, עד 5 ניסיונות
        // =====================================================

        public async Task<VerificationResult> SendEmailVerificationAsync(int userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            if (user.IsEmailVerified)
                return VerificationResult.AlreadyVerified;

            if (await InCooldownAsync(user.Id, ActionTokenPurpose.EmailVerification))
                return VerificationResult.Cooldown;

            string code = await _actionTokens.CreateCodeAsync(user.Id, ActionTokenPurpose.EmailVerification, VerificationCodeLifetime);

            await _notifications.QueueEmailAsync(
                user.Id,
                user.Email,
                "קוד אימות - לתת באהבה",
                $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>קוד האימות שלך הוא: <b>{code}</b><br/>הקוד תקף ל-15 דקות.",
                summary: "נשלח קוד לאימות כתובת המייל");

            return VerificationResult.Success;
        }

        public async Task<VerificationResult> VerifyEmailAsync(int userId, string code)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            var check = await _actionTokens.VerifyCodeAsync(user.Id, ActionTokenPurpose.EmailVerification, code, MaxVerificationAttempts);
            if (check != CodeCheckResult.Valid)
                return ToVerificationResult(check);

            user.IsEmailVerified = true;
            await _userRepository.UpdateAsync(user);
            return VerificationResult.Success;
        }

        public async Task<VerificationResult> SendPhoneVerificationAsync(int userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            if (user.IsPhoneVerified)
                return VerificationResult.AlreadyVerified;

            if (string.IsNullOrWhiteSpace(user.Phone))
                return VerificationResult.SendFailed;

            if (await InCooldownAsync(user.Id, ActionTokenPurpose.PhoneVerification))
                return VerificationResult.Cooldown;

            string code = await _actionTokens.CreateCodeAsync(user.Id, ActionTokenPurpose.PhoneVerification, VerificationCodeLifetime);

            await _notifications.QueueSmsAsync(
                user.Id,
                user.Phone,
                $"לתת באהבה: קוד האימות שלך הוא {code}. תקף ל-15 דקות.",
                summary: "נשלח קוד לאימות מספר הטלפון");

            return VerificationResult.Success;
        }

        public async Task<VerificationResult> VerifyPhoneAsync(int userId, string code)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            var check = await _actionTokens.VerifyCodeAsync(user.Id, ActionTokenPurpose.PhoneVerification, code, MaxVerificationAttempts);
            if (check != CodeCheckResult.Valid)
                return ToVerificationResult(check);

            user.IsPhoneVerified = true;
            await _userRepository.UpdateAsync(user);
            return VerificationResult.Success;
        }

        private async Task<bool> InCooldownAsync(int userId, ActionTokenPurpose purpose)
        {
            var last = await _actionTokens.GetLastIssuedAtAsync(userId, purpose);
            return last.HasValue && DateTime.UtcNow - last.Value < ResendCooldown;
        }

        private static VerificationResult ToVerificationResult(CodeCheckResult check) => check switch
        {
            CodeCheckResult.TooManyAttempts => VerificationResult.TooManyAttempts,
            _ => VerificationResult.InvalidCode
        };

        // =====================================================
        // Users (admin)
        // =====================================================

        public async Task<PagedResponse<UserDto>> GetPageAsync(PageRequest page)
        {
            var result = await _userRepository.GetPageAsync(page.Skip, page.PageSize);
            return new PagedResponse<UserDto>
            {
                Items = _mapper.Map<System.Collections.Generic.List<UserDto>>(result.Items),
                TotalCount = result.TotalCount
            };
        }

        public async Task<UserDto?> GetByIdAsync(int id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            return user == null ? null : _mapper.Map<UserDto>(user);
        }

        // =====================================================
        // החלפת סיסמה ופרופיל אישי (משתמש מחובר)
        // =====================================================

        public async Task<ChangePasswordOutcome> ChangePasswordAsync(int userId, ChangePasswordDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return new ChangePasswordOutcome(ChangePasswordResult.UserNotFound);

            if (string.IsNullOrEmpty(user.PasswordHash) ||
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword) == PasswordVerificationResult.Failed)
                return new ChangePasswordOutcome(ChangePasswordResult.WrongCurrentPassword);

            if (dto.CurrentPassword == dto.NewPassword)
                return new ChangePasswordOutcome(ChangePasswordResult.SameAsCurrent);

            user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;

            // תוקן: החלפת סיסמה לא ביטלה טוקנים שכבר הונפקו. עכשיו החותמת מתחלפת, וכל טוקן ישן נדחה.
            user.SecurityStamp = NewStamp();
            await _userRepository.UpdateAsync(user);

            // אם היה קישור איפוס פתוח - מבטלים אותו
            await _actionTokens.RevokeAsync(user.Id, ActionTokenPurpose.PasswordReset);

            // המשתמש שהחליף את הסיסמה מקבל טוקן חדש ונשאר מחובר
            return new ChangePasswordOutcome(ChangePasswordResult.Success, BuildResponse(user));
        }

        public async Task<MyProfileDto?> GetMyProfileAsync(int userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            return user == null ? null : ToMyProfile(user);
        }

        public async Task<MyProfileDto?> UpdateMyProfileAsync(int userId, UpdateMyProfileDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return null;

            var newPhone = dto.Phone.Trim();
            if (!string.Equals(user.Phone, newPhone, StringComparison.Ordinal))
                user.IsPhoneVerified = false;           // מספר חדש צריך אימות מחדש

            user.Name = dto.Name.Trim();
            user.Phone = newPhone;
            await _userRepository.UpdateAsync(user);

            return ToMyProfile(user);
        }

        private static MyProfileDto ToMyProfile(User user) => new MyProfileDto
        {
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            IsEmailVerified = user.IsEmailVerified,
            IsPhoneVerified = user.IsPhoneVerified,
            MustChangePassword = user.MustChangePassword
        };

        // =====================================================
        // Helpers
        // =====================================================

        public static string NewStamp() => Guid.NewGuid().ToString("N");

        private AuthResponseDto BuildResponse(User user)
        {
            var token = _tokenService.CreateToken(user);

            return new AuthResponseDto
            {
                Token = token.Token,
                Role = user.Role.ToString(),
                ExpiresAt = token.ExpiresAt,
                MustChangePassword = user.MustChangePassword
            };
        }
    }
}
