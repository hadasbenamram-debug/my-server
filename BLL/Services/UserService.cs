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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace BLL.Services
{
    public sealed class UserService : IUserService
    {
        private const string InvalidCredentialsMessage = "שם משתמש או סיסמה שגויים.";
        private const string InvalidLoginLinkMessage =
            "קישור הכניסה אינו בתוקף (פג תוקפו או שכבר השתמשו בו). אפשר להתחבר כרגיל עם המייל והסיסמה.";
        private const int MaxVerificationAttempts = 5;
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromMinutes(15);

        private readonly IUserRepository _userRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly ILoginLockoutService _lockout;
        private readonly IActionTokenService _actionTokens;
        private readonly INotificationService _notifications;
        private readonly ITokenService _tokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserService> _logger;
        private readonly IMapper _mapper;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IRegistrationNoticeService _registrationNotices;
        private readonly ISmsSender _sms;
        // תיקון 65: Twilio Verify (אם מוגדר) - Twilio יוצרת, שולחת ובודקת את קוד האימות לטלפון
        private readonly IPhoneVerifier? _phoneVerifier;

        // hash קבוע לבדיקה מול מייל שלא קיים, כדי שזמן התגובה יהיה דומה (לא חושף אילו מיילים רשומים)
        private static readonly Lazy<string> DummyHash =
            new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString()));

        public UserService(
            IUserRepository userRepository,
            INgoRepository ngoRepository,
            ISupplierRepository supplierRepository,
            IVolunteerRepository volunteerRepository,
            ILoginLockoutService lockout,
            IActionTokenService actionTokens,
            INotificationService notifications,
            ITokenService tokenService,
            IConfiguration configuration,
            ILogger<UserService> logger,
            IMapper mapper,
            IPasswordHasher<User> passwordHasher,
            IRegistrationNoticeService registrationNotices,
            ISmsSender sms,
            IPhoneVerifier? phoneVerifier = null)
        {
            _phoneVerifier = phoneVerifier;
            _passwordHasher = passwordHasher;
            _registrationNotices = registrationNotices;
            _sms = sms;
            _userRepository = userRepository;
            _ngoRepository = ngoRepository;
            _supplierRepository = supplierRepository;
            _volunteerRepository = volunteerRepository;
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

        // תוקן: הרשמה עם מייל שכבר רשום החזירה "כבר קיים משתמש עם כתובת אימייל זו" (409) - כך שאפשר היה
        // לבדוק אילו מיילים רשומים. עכשיו: אותה תשובה כמו בהרשמה חדשה, ובעל החשבון מקבל מייל (רק הוא רואה אותו).
        public async Task<RegistrationResult> RegisterAsync(RegisterDto dto)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            var existing = await _userRepository.GetByEmailAsync(email);
            if (existing != null)
                return await ExistingAccountAsync(existing, dto.Password);

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
            {
                var winner = await _userRepository.GetByEmailAsync(email);
                if (winner != null)
                    await _registrationNotices.NotifyExistingAccountAsync(winner, null);
                return RegistrationResult.ExistingAccount;
            }

            // ההרשמה רק יוצרת את המשתמש. את הטוקן מקבלים בהתחברות (LoginAsync)
            return RegistrationResult.Created;
        }

        // מייל כבר רשום: לא יוצרים כלום ולא זורקים חריגה. מחשבים hash כמו בהרשמה חדשה,
        // כדי שזמן התגובה יהיה דומה, ושולחים לבעל החשבון מייל "כבר יש לך חשבון".
        private async Task<RegistrationResult> ExistingAccountAsync(User existing, string password)
        {
            _ = _passwordHasher.HashPassword(new User(), password);
            await _registrationNotices.NotifyExistingAccountAsync(existing, null);
            return RegistrationResult.ExistingAccount;
        }

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

            // --- approval status for NGOs, suppliers and volunteers ---
            // (נבדק רק אחרי שהסיסמה נכונה, כך שמי שלא יודע את הסיסמה לא לומד מה מצב החשבון)
            var roles = await GetHeldRolesAsync(user);
            var blocked = CheckApproval(roles);
            if (blocked != null)
                return blocked;

            await _lockout.RecordAsync(email, ipAddress, user.Id, success: true);

            return AuthResult.Success(BuildResponse(user, roles));
        }

        // =====================================================
        // תפקידים - תיקון 10: לחשבון אחד (מייל אחד) יכולים להיות כמה פרופילים: מתנדב, ספק, עמותה
        // =====================================================

        private sealed record HeldRole(UserRole Role, ApprovalStatus Status);

        // כל התפקידים של החשבון, התפקיד הראשי (User.Role) ראשון. מנהל הוא תפקיד בלי פרופיל.
        private async Task<List<HeldRole>> GetHeldRolesAsync(User user)
        {
            var roles = new List<HeldRole>();
            if (user.Role == UserRole.Admin)
                roles.Add(new HeldRole(UserRole.Admin, ApprovalStatus.Approved));

            var ngo = await _ngoRepository.GetByUserIdAsync(user.Id);
            if (ngo != null)
                roles.Add(new HeldRole(UserRole.Ngo, ngo.ApprovalStatus));

            var supplier = await _supplierRepository.GetByUserIdAsync(user.Id);
            if (supplier != null)
                roles.Add(new HeldRole(UserRole.Supplier, supplier.ApprovalStatus));

            var volunteer = await _volunteerRepository.GetByUserIdAsync(user.Id);
            if (volunteer != null)
                roles.Add(new HeldRole(UserRole.Volunteer, volunteer.ApprovalStatus));

            return roles.OrderBy(r => r.Role == user.Role ? 0 : 1).ToList();
        }

        // האם החשבון רשאי להיכנס. null = מותר. משותף להתחברות רגילה ולכניסה מהקישור שבמייל האישור.
        // תיקון 10: נכנסים אם לפחות תפקיד אחד מאושר - בקשה לתפקיד נוסף שממתינה לאישור (או נדחתה)
        // לא חוסמת את התפקיד שכבר מאושר. חוסמים רק כשאין אף תפקיד מאושר: עמותה / ספק / מתנדב
        // שממתינים לאישור או שנדחו לא נכנסים (תוקן בעבר: מתנדב יכול היה להתחבר עוד לפני האישור).
        private static AuthResult? CheckApproval(IReadOnlyList<HeldRole> roles)
        {
            if (roles.Count == 0 || roles.Any(r => r.Status == ApprovalStatus.Approved))
                return null;

            var blocking = roles.FirstOrDefault(r => r.Status == ApprovalStatus.Pending) ?? roles[0];
            bool pending = blocking.Status == ApprovalStatus.Pending;

            return blocking.Role switch
            {
                UserRole.Ngo => pending
                    ? AuthResult.Fail(AuthError.NgoPendingApproval, "חשבון העמותה שלך ממתין לאישור מנהל מערכת.")
                    : AuthResult.Fail(AuthError.NgoRejected, "בקשת הצטרפות העמותה שלך נדחתה."),
                UserRole.Supplier => pending
                    ? AuthResult.Fail(AuthError.SupplierPendingApproval, "חשבון הספק שלך ממתין לאישור מנהל מערכת.")
                    : AuthResult.Fail(AuthError.SupplierRejected, "בקשת הצטרפות הספק שלך נדחתה."),
                _ => pending
                    ? AuthResult.Fail(AuthError.VolunteerPendingApproval,
                        "בקשת ההצטרפות שלך כמתנדב/ת ממתינה לאישור מנהל מערכת. אחרי האישור יישלח אליך מייל ותוכל/י להתחבר.")
                    : AuthResult.Fail(AuthError.VolunteerRejected, "בקשת ההצטרפות שלך כמתנדב/ת נדחתה.")
            };
        }

        // התפקיד שבו נכנסים: המבוקש (למשל מהקישור במייל האישור) אם הוא מאושר, אחרת הראשי, אחרת הראשון שמאושר
        private static UserRole ActiveRole(User user, IReadOnlyList<HeldRole> roles, UserRole? requested = null)
        {
            var approved = roles.Where(r => r.Status == ApprovalStatus.Approved).Select(r => r.Role).ToList();
            if (requested.HasValue && approved.Contains(requested.Value))
                return requested.Value;
            if (approved.Count == 0 || approved.Contains(user.Role))
                return user.Role;
            return approved[0];
        }

        // =====================================================
        // כניסה מהקישור שבמייל "הבקשה שלך אושרה" - בלי מסך התחברות
        // =====================================================

        // הקישור אישי, חד-פעמי ותקף 3 ימים (במסד נשמר רק Hash שלו). מי שעדיין עם הסיסמה הזמנית
        // מההרשמה בוחר סיסמה אישית באותו מסך (בלי להקליד את הסיסמה הזמנית) - ורק אז הקישור מנוצל.
        public async Task<LinkLoginOutcome> LinkLoginAsync(LinkLoginDto dto, string? ipAddress = null)
        {
            var token = await _actionTokens.FindValidLinkTokenAsync(ActionTokenPurpose.LoginLink, dto.Token);
            if (token?.UserId == null)
                return new LinkLoginOutcome(LinkLoginStatus.InvalidLink, Message: InvalidLoginLinkMessage);

            var user = await _userRepository.GetByIdAsync(token.UserId.Value);
            if (user == null)
                return new LinkLoginOutcome(LinkLoginStatus.InvalidLink, Message: InvalidLoginLinkMessage);

            var roles = await GetHeldRolesAsync(user);
            var blocked = CheckApproval(roles);
            if (blocked != null)
                return new LinkLoginOutcome(LinkLoginStatus.Blocked, Error: blocked.Error, Message: blocked.Message);

            if (user.MustChangePassword)
            {
                // הקישור עוד לא נוצל - הלקוח מציג "בחירת סיסמה" ושולח שוב עם NewPassword
                if (string.IsNullOrEmpty(dto.NewPassword))
                    return new LinkLoginOutcome(LinkLoginStatus.RequiresPassword,
                        new LinkLoginResponseDto { RequiresPassword = true, Email = user.Email });

                user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword);
                user.MustChangePassword = false;
                user.SecurityStamp = NewStamp();          // טוקנים ישנים (עם הסיסמה הזמנית) מפסיקים לעבוד
                await _userRepository.UpdateAsync(user);
                await _actionTokens.RevokeAsync(user.Id, ActionTokenPurpose.PasswordReset);
            }

            // תיקון 51: הקישור נשלח רק למייל של המשתמש - מי שפתח אותו הוכיח שהמייל שלו
            if (!user.IsEmailVerified)
            {
                user.IsEmailVerified = true;
                await _userRepository.UpdateAsync(user);
            }

            await _actionTokens.ConsumeAsync(token);
            await _lockout.RecordAsync(user.Email, ipAddress, user.Id, success: true);

            var role = token.TargetId is int roleValue && Enum.IsDefined(typeof(RequestedRole), roleValue)
                ? (RequestedRole?)roleValue
                : null;

            // נכנסים ישר לתפקיד שאושר עכשיו (גם כשלחשבון יש עוד תפקידים)
            var auth = BuildResponse(user, roles, role.HasValue ? ToUserRole(role.Value) : null);

            return new LinkLoginOutcome(LinkLoginStatus.Success, new LinkLoginResponseDto
            {
                Token = auth.Token,
                Role = auth.Role,
                Roles = auth.Roles,
                ExpiresAt = auth.ExpiresAt,
                MustChangePassword = auth.MustChangePassword,
                NextPath = role.HasValue ? FrontendLinks.RoleHomePath(role.Value) : null,
                Email = user.Email
            });
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
            string? resetUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? null
                : $"{baseUrl.TrimEnd('/')}/app/reset-password?token={WebUtility.UrlEncode(token)}";

            // תיקון 37: מייל מעוצב - כפתור לבחירת סיסמה חדשה, והקוד בתיבה בולטת
            await _notifications.QueueEmailAsync(
                user.Id,
                user.Email,
                "איפוס סיסמה - לתת באהבה",
                EmailTemplate.Greeting(user.Name) +
                EmailTemplate.Paragraph("התקבלה בקשה לאיפוס הסיסמה שלך.") +
                (resetUrl == null ? "" : EmailTemplate.Button(resetUrl, "בחירת סיסמה חדשה")) +
                EmailTemplate.Paragraph(resetUrl == null ? "קוד האיפוס:" : "אפשר גם להזין את קוד האיפוס במסך איפוס הסיסמה:") +
                EmailTemplate.Code(token) +
                EmailTemplate.Note("הקוד תקף ל-30 דקות ולשימוש אחד. אם לא ביקשת איפוס - אפשר להתעלם מההודעה, והסיסמה לא תשתנה.") +
                EmailTemplate.Signature(),
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
            user.IsEmailVerified = true;              // תיקון 51: קוד האיפוס נשלח רק למייל - המייל אומת
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
                EmailTemplate.Greeting(user.Name) +
                EmailTemplate.Paragraph("קוד האימות של כתובת המייל שלך:") +
                EmailTemplate.Code(code) +
                EmailTemplate.Note("הקוד תקף ל-15 דקות. אם לא ביקשת קוד אימות - אפשר להתעלם מההודעה.") +
                EmailTemplate.Signature(),
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

        public async Task<VerificationResult> SendPhoneVerificationAsync(int userId, PhoneVerificationMethod method = PhoneVerificationMethod.Sms)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            if (user.IsPhoneVerified)
                return VerificationResult.AlreadyVerified;

            if (string.IsNullOrWhiteSpace(user.Phone))
                return VerificationResult.SendFailed;

            var byCall = method == PhoneVerificationMethod.VoiceCall;

            // תיקון 65: Twilio Verify מוגדר - Twilio יוצרת את הקוד ושולחת אותו בעצמה (מסרון או שיחה קולית)
            if (_phoneVerifier is { IsConfigured: true } verifier)
            {
                if (await InCooldownAsync(user.Id, ActionTokenPurpose.PhoneVerification))
                    return VerificationResult.Cooldown;

                var started = await verifier.StartAsync(user.Phone, byCall);
                if (started != PhoneVerifyStart.Sent)
                    return started == PhoneVerifyStart.CannotReceiveSms ? VerificationResult.CannotReceiveSms : VerificationResult.SendFailed;

                // רישום השליחה, כדי שההגבלה של דקה בין שליחות תחול גם כאן (הקוד עצמו נשאר אצל Twilio ולא נבדק מולנו)
                await _actionTokens.CreateCodeAsync(user.Id, ActionTokenPurpose.PhoneVerification, VerificationCodeLifetime);
                return VerificationResult.Success;
            }

            // תוקן: בלי ספק SMS (ברירת המחדל הייתה Sms:Provider = "Log") הקוד לא נשלח לטלפון, אבל המשתמש קיבל
            // "קוד אימות נשלח במסרון". עכשיו: בלי ספק מוגדר - הודעה ברורה שה-SMS לא זמין, ושום דבר לא נכנס לתור.
            // תיקון 64: גם בשיחה קולית - אם אין ספק שיכול להתקשר, אומרים את זה ולא "התקשרנו"
            var mode = byCall ? _sms.VoiceMode : _sms.Mode;
            if (mode == SmsMode.NotConfigured)
            {
                _logger.LogWarning("Phone verification requested ({Method}) but it is not configured (Sms:Provider).", method);
                return VerificationResult.SmsNotConfigured;
            }

            if (await InCooldownAsync(user.Id, ActionTokenPurpose.PhoneVerification))
                return VerificationResult.Cooldown;

            string code = await _actionTokens.CreateCodeAsync(user.Id, ActionTokenPurpose.PhoneVerification, VerificationCodeLifetime);

            if (byCall)
                await _notifications.QueueVoiceCallAsync(
                    user.Id,
                    user.Phone,
                    VoiceVerificationScript(code),
                    summary: "שיחה קולית עם קוד לאימות מספר הטלפון");
            else
                await _notifications.QueueSmsAsync(
                    user.Id,
                    user.Phone,
                    $"לתת באהבה: קוד האימות שלך הוא {code}. תקף ל-15 דקות.",
                    summary: "נשלח קוד לאימות מספר הטלפון");

            return mode == SmsMode.DevelopmentLog ? VerificationResult.SentToLogOnly : VerificationResult.Success;
        }

        // תיקון 64: מה שמוקרא בשיחה. הספרות מופרדות בפסיקים כדי שיוקראו אחת-אחת ("שלוש, שבע..." ולא "שלוש מאות...").
        // הספק מקריא את ההודעה פעמיים.
        public static string VoiceVerificationScript(string code) =>
            $"שלום, כאן לתת באהבה. קוד האימות שלך הוא: {string.Join<char>(", ", code)}. הקוד תקף לרבע שעה.";

        public async Task<VerificationResult> VerifyPhoneAsync(int userId, string code)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return VerificationResult.UserNotFound;

            // תיקון 65: עם Twilio Verify - Twilio בודקת את הקוד שהיא שלחה
            if (_phoneVerifier is { IsConfigured: true } verifier)
            {
                if (string.IsNullOrWhiteSpace(user.Phone))
                    return VerificationResult.InvalidCode;

                var verdict = await verifier.CheckAsync(user.Phone, (code ?? string.Empty).Trim());
                if (verdict != PhoneVerifyCheck.Approved)
                {
                    return verdict switch
                    {
                        PhoneVerifyCheck.TooManyAttempts => VerificationResult.TooManyAttempts,
                        PhoneVerifyCheck.Failed => VerificationResult.SendFailed,
                        _ => VerificationResult.InvalidCode
                    };
                }

                await _actionTokens.RevokeAsync(user.Id, ActionTokenPurpose.PhoneVerification);
                user.IsPhoneVerified = true;
                await _userRepository.UpdateAsync(user);
                return VerificationResult.Success;
            }

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

        // תיקון 51: מנהל מסמן אימות ידני (למשל אחרי שיחה עם המשתמש, או כשאין ספק SMS לשליחת קוד לטלפון)
        public async Task<UserDto?> SetVerificationAsync(int userId, UpdateVerificationDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return null;

            if (dto.IsEmailVerified.HasValue)
                user.IsEmailVerified = dto.IsEmailVerified.Value;
            if (dto.IsPhoneVerified.HasValue)
                user.IsPhoneVerified = dto.IsPhoneVerified.Value;

            await _userRepository.UpdateAsync(user);
            return _mapper.Map<UserDto>(user);
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
            return new ChangePasswordOutcome(ChangePasswordResult.Success, BuildResponse(user, await GetHeldRolesAsync(user)));
        }

        // תיקון 11: המתנדב אישר מסלול מהקישור במייל / ב-SMS - נכנס ישר לאזור המתנדב, בלי מסך התחברות.
        // הקישור אישי, חד-פעמי (נוצל באישור עצמו) ותקף רק עד סוף זמן המענה. מי שעדיין עם הסיסמה הזמנית,
        // או בלי אף תפקיד מאושר, לא נכנס כך - הוא מתחבר כרגיל (וכך גם בוחר סיסמה אישית).
        public async Task<AuthResponseDto?> CreateLinkSessionAsync(int userId, FoodRescue.DTOs.RequestedRole role)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || user.MustChangePassword)
                return null;

            var roles = await GetHeldRolesAsync(user);
            if (CheckApproval(roles) != null)
                return null;

            var requested = role switch
            {
                FoodRescue.DTOs.RequestedRole.Volunteer => UserRole.Volunteer,
                FoodRescue.DTOs.RequestedRole.Supplier => UserRole.Supplier,
                _ => UserRole.Ngo
            };
            return BuildResponse(user, roles, requested);
        }

        public async Task<MyProfileDto?> GetMyProfileAsync(int userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            return user == null ? null : ToMyProfile(user, await GetHeldRolesAsync(user), await GetKnownIdNumberAsync(user.Id));
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

            return ToMyProfile(user, await GetHeldRolesAsync(user), await GetKnownIdNumberAsync(user.Id));
        }

        // תיקון 32: ת"ז שכבר שמורה בחשבון (מתנדב, או ספק שהוא עוסק מורשה / פטור) - כדי לא לבקש אותה שוב בהוספת תפקיד
        private async Task<string?> GetKnownIdNumberAsync(int userId) =>
            AccountIdNumber.Find(
                await _volunteerRepository.GetByUserIdAsync(userId),
                await _supplierRepository.GetByUserIdAsync(userId));

        private static MyProfileDto ToMyProfile(User user, IReadOnlyList<HeldRole> roles, string? knownIdNumber) => new MyProfileDto
        {
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            IsEmailVerified = user.IsEmailVerified,
            IsPhoneVerified = user.IsPhoneVerified,
            MustChangePassword = user.MustChangePassword,
            // תיקון 10: כל התפקידים של החשבון והסטטוס של כל אחד (לעמוד הפרופיל). מנהל לא מוסיף תפקידים.
            Roles = roles.Select(r => new AccountRoleDto { Role = r.Role.ToString(), Status = r.Status.ToString() }).ToList(),
            CanAddRoles = user.Role != UserRole.Admin,
            // רק גרסה מוסתרת - המספר המלא לא יוצא מהשרת
            KnownIdNumberMasked = knownIdNumber == null ? null : AccountIdNumber.Mask(knownIdNumber)
        };

        // =====================================================
        // Helpers
        // =====================================================

        public static string NewStamp() => Guid.NewGuid().ToString("N");

        // Role = התפקיד שבו נכנסים; Roles = כל התפקידים המאושרים (הלקוח מציג מעבר ביניהם).
        // ההרשאות בשרת לא תלויות בתפקיד שנבחר: פעולות מתנדב / ספק / עמותה נבדקות לפי הפרופיל של המשתמש.
        private AuthResponseDto BuildResponse(User user, IReadOnlyList<HeldRole> roles, UserRole? requestedRole = null)
        {
            var token = _tokenService.CreateToken(user);
            var approved = roles.Where(r => r.Status == ApprovalStatus.Approved).Select(r => r.Role.ToString()).ToList();
            var active = ActiveRole(user, roles, requestedRole).ToString();
            if (!approved.Contains(active))
                approved.Insert(0, active);

            return new AuthResponseDto
            {
                Token = token.Token,
                Role = active,
                Roles = approved,
                ExpiresAt = token.ExpiresAt,
                MustChangePassword = user.MustChangePassword
            };
        }

        private static UserRole ToUserRole(RequestedRole role) => role switch
        {
            RequestedRole.Volunteer => UserRole.Volunteer,
            RequestedRole.Supplier => UserRole.Supplier,
            _ => UserRole.Ngo
        };
    }
}
