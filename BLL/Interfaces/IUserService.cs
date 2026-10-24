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
        SupplierRejected
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
        TooManyAttempts
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

    public interface IUserService
    {
        /// <summary>Creates a regular user (Role is always User). No token here: the token is issued only by LoginAsync.</summary>
        Task<AuthResult> RegisterAsync(RegisterDto dto);

        /// <summary>Validates credentials, enforces login lock-out (per e-mail + IP) and NGO/Supplier approval status.</summary>
        Task<AuthResult> LoginAsync(LoginDto dto, string? ipAddress = null);

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

        /// <summary>Updates name and phone only. Email is the login identifier and is not changed here.</summary>
        Task<MyProfileDto?> UpdateMyProfileAsync(int userId, UpdateMyProfileDto dto);

        Task<PagedResponse<UserDto>> GetPageAsync(PageRequest page);
        Task<UserDto?> GetByIdAsync(int id);
    }
}
