using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public enum AuthError
    {
        None,
        EmailTaken,
        InvalidCredentials,
        LockedOut,
        NgoPendingApproval,
        NgoRejected,
        SupplierPendingApproval,
        SupplierRejected,
        // מתנדב שנרשם לא נכנס למערכת עד שמנהל מאשר אותו (כמו ספק ועמותה)
        VolunteerPendingApproval,
        VolunteerRejected
    }

    // תוצאת שליחת קוד אימות / אימות מייל או טלפון. הקונטרולר ממיר אותה לקוד HTTP ולהודעה.
    public enum VerificationResult
    {
        Success,
        UserNotFound,
        AlreadyVerified,
        Cooldown,
        SendFailed,
        InvalidCode,
        TooManyAttempts,
        // SMS: אין ספק SMS מוגדר בשרת - הקוד לא נשלח (בעבר הוחזר "נשלח" למרות שלא נשלח כלום)
        SmsNotConfigured,
        // SMS בסביבת פיתוח בלי ספק: לא נשלח בפועל, הקוד נכתב ביומן השרת
        SentToLogOnly
    }

    public enum ChangePasswordResult
    {
        Success,
        UserNotFound,
        WrongCurrentPassword,
        SameAsCurrent
    }

    public sealed class AuthResult
    {
        public AuthError Error { get; private init; }
        public string? Message { get; private init; }
        public AuthResponseDto? Response { get; private init; }

        public bool Succeeded => Error == AuthError.None;

        // בהרשמה אין טוקן (Response נשאר null). בהתחברות מוחזר הטוקן.
        public static AuthResult Success(AuthResponseDto? response = null) =>
            new() { Error = AuthError.None, Response = response };

        public static AuthResult Fail(AuthError error, string message) =>
            new() { Error = error, Message = message };
    }

    public sealed record ChangePasswordOutcome(ChangePasswordResult Result, AuthResponseDto? NewToken = null);

    // כניסה מהקישור שבמייל האישור
    public enum LinkLoginStatus
    {
        Success,
        RequiresPassword,   // עדיין עם הסיסמה הזמנית: לבחור סיסמה אישית ולשלוח שוב (הקישור עוד לא נוצל)
        InvalidLink,        // פג / נוצל / שגוי
        Blocked             // החשבון לא מאושר כרגע (ממתין / נדחה)
    }

    public sealed record LinkLoginOutcome(
        LinkLoginStatus Status,
        LinkLoginResponseDto? Response = null,
        AuthError Error = AuthError.None,
        string? Message = null);

    public interface IUserService
    {
        /// <summary>
        /// Creates a regular user (Role is always User). No token here: the token is issued only by LoginAsync.
        /// An e-mail that is already registered creates nothing and e-mails the owner; the caller answers both cases the same way.
        /// </summary>
        Task<RegistrationResult> RegisterAsync(RegisterDto dto);

        /// <summary>Validates credentials, enforces login lock-out (per e-mail + IP) and NGO/Supplier approval status.</summary>
        Task<AuthResult> LoginAsync(LoginDto dto, string? ipAddress = null);

        /// <summary>One-time login link from the "approved" e-mail: issues a token without the login form.</summary>
        Task<LinkLoginOutcome> LinkLoginAsync(LinkLoginDto dto, string? ipAddress = null);

        /// <summary>Always completes silently, so the caller can return one generic message.</summary>
        Task ForgotPasswordAsync(ForgotPasswordDto dto);

        /// <summary>Returns false when the reset token is wrong, used or expired.</summary>
        Task<bool> ResetPasswordAsync(ResetPasswordDto dto);

        Task<VerificationResult> SendEmailVerificationAsync(int userId);
        Task<VerificationResult> VerifyEmailAsync(int userId, string code);

        Task<VerificationResult> SendPhoneVerificationAsync(int userId);
        Task<VerificationResult> VerifyPhoneAsync(int userId, string code);

        /// <summary>Changes the password; all previously issued tokens stop working and a new one is returned.</summary>
        Task<ChangePasswordOutcome> ChangePasswordAsync(int userId, ChangePasswordDto dto);

        Task<MyProfileDto?> GetMyProfileAsync(int userId);

        // תיקון 11: כניסה למערכת בלי מסך התחברות אחרי פעולה מקישור אישי וחד-פעמי (למשל אישור מסלול מה-SMS).
        // null = לא נכנסים כך (סיסמה זמנית / אין תפקיד מאושר) - מתחברים כרגיל.
        Task<AuthResponseDto?> CreateLinkSessionAsync(int userId, RequestedRole role);

        /// <summary>Updates name and phone only. Email is the login identifier and is not changed here.</summary>
        Task<MyProfileDto?> UpdateMyProfileAsync(int userId, UpdateMyProfileDto dto);

        Task<PagedResponse<UserDto>> GetPageAsync(PageRequest page);
        Task<UserDto?> GetByIdAsync(int id);
    }
}
