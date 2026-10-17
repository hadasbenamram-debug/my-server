using BLL.Interfaces;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ApiControllerBase
    {
        private const string ForgotPasswordGenericMessage =
            "אם כתובת האימייל קיימת במערכת, נשלח אליה קישור לאיפוס סיסמה.";

        private readonly IUserService _userService;

        public AuthController(IUserService userService)
        {
            _userService = userService;
        }

        // רישום משתמש חדש במערכת (מחזיר טוקן התחברות מיד, כמו קודם)
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var result = await _userService.RegisterAsync(dto);

            if (!result.Succeeded)
                return Conflict(Error(409, result.Message));

            return Ok(result.Response);
        }

        // התחברות. הנעילה אחרי ניסיונות כושלים נשמרת במסד, לפי כתובת מייל + IP
        // (בעבר לפי מייל בלבד - כך שכל אחד יכול היה לנעול חשבון של מישהו אחר).
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var ip = HttpContext?.Connection.RemoteIpAddress?.ToString();
            var result = await _userService.LoginAsync(dto, ip);

            if (result.Succeeded)
                return Ok(result.Response);

            return result.Error switch
            {
                AuthError.LockedOut => StatusCode(423, Error(423, result.Message, "LockedOut")),

                AuthError.NgoPendingApproval or
                AuthError.NgoRejected or
                AuthError.SupplierPendingApproval or
                AuthError.SupplierRejected => StatusCode(403, Error(403, result.Message, result.Error.ToString())),

                _ => Unauthorized(Error(401, result.Message, "InvalidCredentials"))
            };
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            await _userService.ForgotPasswordAsync(dto);
            return Message(ForgotPasswordGenericMessage);
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (!await _userService.ResetPasswordAsync(dto))
                return BadRequest(Error(400, "הטוקן שגוי או שפג תוקפו."));

            return Message("הסיסמה אופסה בהצלחה. אפשר להתחבר עם הסיסמה החדשה.");
        }

        // החלפת סיסמה ע"י משתמש מחובר. כל הטוקנים הקודמים מפסיקים לעבוד, ולכן מוחזר טוקן חדש.
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var outcome = await _userService.ChangePasswordAsync(RequireUserId(), dto);

            return outcome.Result switch
            {
                ChangePasswordResult.Success => Ok(new
                {
                    message = "הסיסמה שונתה בהצלחה.",
                    token = outcome.NewToken?.Token,
                    role = outcome.NewToken?.Role,
                    expiresAt = outcome.NewToken?.ExpiresAt,
                    mustChangePassword = false
                }),
                ChangePasswordResult.WrongCurrentPassword => BadRequest(Error(400, "הסיסמה הנוכחית שגויה.")),
                ChangePasswordResult.SameAsCurrent => BadRequest(Error(400, "הסיסמה החדשה זהה לנוכחית.")),
                _ => Unauthorized()
            };
        }

        // ---------------------------------------------------------------
        // אימות מייל וטלפון (הלוגיקה ב-UserService; הקודים נשמרים מגובבים במסד)
        // ---------------------------------------------------------------

        [HttpPost("send-email-verification")]
        [Authorize]
        public async Task<IActionResult> SendEmailVerification() =>
            SendResult(await _userService.SendEmailVerificationAsync(RequireUserId()),
                "קוד האימות נשלח לכתובת המייל שלך.", "כתובת האימייל כבר מאומתת.");

        [HttpPost("verify-email")]
        [Authorize]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyCodeDto model) =>
            VerifyResult(await _userService.VerifyEmailAsync(RequireUserId(), model.Code), "כתובת האימייל אומתה בהצלחה!");

        // חדש: אימות מספר טלפון ב-SMS
        [HttpPost("send-phone-verification")]
        [Authorize]
        public async Task<IActionResult> SendPhoneVerification() =>
            SendResult(await _userService.SendPhoneVerificationAsync(RequireUserId()),
                "קוד אימות נשלח במסרון למספר הטלפון שלך.", "מספר הטלפון כבר מאומת.");

        [HttpPost("verify-phone")]
        [Authorize]
        public async Task<IActionResult> VerifyPhone([FromBody] VerifyCodeDto model) =>
            VerifyResult(await _userService.VerifyPhoneAsync(RequireUserId(), model.Code), "מספר הטלפון אומת בהצלחה!");

        private IActionResult SendResult(VerificationResult result, string success, string alreadyVerified) => result switch
        {
            VerificationResult.Success => Message(success),
            VerificationResult.UserNotFound => Unauthorized(),
            VerificationResult.AlreadyVerified => BadRequest(Error(400, alreadyVerified)),
            VerificationResult.Cooldown => StatusCode(429, Error(429, "נשלח קוד לאחרונה. נסו שוב בעוד דקה.")),
            _ => StatusCode(503, Error(503, "לא ניתן לשלוח את הקוד כרגע. נסו שוב מאוחר יותר."))
        };

        private IActionResult VerifyResult(VerificationResult result, string success) => result switch
        {
            VerificationResult.Success => Message(success),
            VerificationResult.UserNotFound => Unauthorized(),
            VerificationResult.TooManyAttempts => BadRequest(Error(400, "בוצעו יותר מדי ניסיונות שגויים. יש לבקש קוד חדש.")),
            _ => BadRequest(Error(400, "הקוד שגוי או שפג תוקפו."))
        };

        private static Infrastructure.ApiError Error(int status, string? message, string? code = null) =>
            new() { Status = status, Message = message ?? string.Empty, Code = code };
    }
}
