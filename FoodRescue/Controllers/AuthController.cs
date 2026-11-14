using BLL.Interfaces;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

        // רישום משתמש חדש במערכת. ההרשמה רק יוצרת את המשתמש - את הטוקן מקבלים בהתחברות (api/auth/login).
        // תוקן: מייל שכבר רשום קיבל 409 "כבר קיים משתמש" (חשף אילו מיילים רשומים). עכשיו התשובה זהה תמיד,
        // ובעל החשבון הקיים מקבל מייל.
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            await _userService.RegisterAsync(dto);
            return Message(RegistrationAcceptedMessage);
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
                AuthError.SupplierRejected or
                AuthError.VolunteerPendingApproval or
                AuthError.VolunteerRejected => StatusCode(403, Error(403, result.Message, result.Error.ToString())),

                _ => Unauthorized(Error(401, result.Message, "InvalidCredentials"))
            };
        }

        // חדש: כניסה מהקישור שבמייל "הבקשה שלך אושרה" - בלי לעבור במסך ההתחברות.
        // הטוקן חד-פעמי ונשלח בגוף הבקשה (לא נשאר בכתובת). מי שעדיין עם הסיסמה הזמנית מקבל
        // requiresPassword=true, בוחר סיסמה אישית ושולח שוב עם newPassword.
        [HttpPost("link-login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> LinkLogin([FromBody] LinkLoginDto dto)
        {
            var ip = HttpContext?.Connection.RemoteIpAddress?.ToString();
            var outcome = await _userService.LinkLoginAsync(dto, ip);

            return outcome.Status switch
            {
                LinkLoginStatus.Success or LinkLoginStatus.RequiresPassword => Ok(outcome.Response),
                LinkLoginStatus.Blocked => StatusCode(403, Error(403, outcome.Message, outcome.Error.ToString())),
                _ => BadRequest(Error(400, outcome.Message, "InvalidLink"))
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

        // תיקון 67: איפוס סיסמה בקוד לטלפון המאומת - { "email": "...", "method": "sms" | "call" }.
        // כמו במייל: אותה תשובה גם כשהחשבון לא קיים או שהטלפון שלו לא מאומת.
        [HttpPost("forgot-password-phone")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPasswordPhone([FromBody] ForgotPasswordPhoneDto dto)
        {
            var byCall = string.Equals(dto.Method, "call", System.StringComparison.OrdinalIgnoreCase);
            var result = await _userService.SendPasswordResetCodeAsync(dto.Email,
                byCall ? PhoneVerificationMethod.VoiceCall : PhoneVerificationMethod.Sms);

            if (result == VerificationResult.SmsNotConfigured)
                return StatusCode(503, Error(503, byCall
                    ? "שיחות קוליות עדיין לא הוגדרו במערכת. אפשר לנסות במסרון או לאפס דרך המייל."
                    : "שליחת מסרונים עדיין לא הוגדרה במערכת. אפשר לאפס את הסיסמה דרך המייל.",
                    byCall ? "VoiceNotConfigured" : "SmsNotConfigured"));

            return Message(byCall
                ? "אם קיים חשבון עם כתובת האימייל הזו ומספר טלפון מאומת, נתקשר אליו ונקריא קוד לאיפוס הסיסמה. הקוד תקף ל-10 דקות."
                : "אם קיים חשבון עם כתובת האימייל הזו ומספר טלפון מאומת, נשלח אליו מסרון עם קוד לאיפוס הסיסמה. הקוד תקף ל-10 דקות.");
        }

        [HttpPost("reset-password-phone")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPasswordPhone([FromBody] ResetPasswordPhoneDto dto) =>
            (await _userService.ResetPasswordByPhoneAsync(dto)) switch
            {
                VerificationResult.Success => Message("הסיסמה אופסה בהצלחה. אפשר להתחבר עם הסיסמה החדשה."),
                VerificationResult.TooManyAttempts => BadRequest(Error(400, "בוצעו יותר מדי ניסיונות שגויים. יש לבקש קוד חדש.")),
                VerificationResult.SendFailed => StatusCode(503, Error(503, "לא ניתן לבדוק את הקוד כרגע. נסו שוב בעוד רגע.")),
                _ => BadRequest(Error(400, "הקוד שגוי או שפג תוקפו."))
            };

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
                    roles = outcome.NewToken?.Roles,
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
        // תיקון 64: או בשיחה קולית - { "method": "call" }. בלי גוף בקשה = מסרון, כמו קודם.
        [HttpPost("send-phone-verification")]
        [Authorize]
        public async Task<IActionResult> SendPhoneVerification(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SendPhoneVerificationDto? model = null)
        {
            var byCall = string.Equals(model?.Method, "call", System.StringComparison.OrdinalIgnoreCase);
            var result = await _userService.SendPhoneVerificationAsync(RequireUserId(),
                byCall ? PhoneVerificationMethod.VoiceCall : PhoneVerificationMethod.Sms);

            if (!byCall)
                return SendResult(result, "קוד אימות נשלח במסרון למספר הטלפון שלך.", "מספר הטלפון כבר מאומת.");

            return result switch
            {
                VerificationResult.SentToLogOnly => Message(
                    "סביבת פיתוח: לא הוגדר ספק שיחות, ולכן השיחה לא בוצעה בפועל. הקוד נכתב ביומן (Console) של השרת."),
                VerificationResult.SmsNotConfigured => StatusCode(503, Error(503,
                    "שיחות קוליות עדיין לא הוגדרו במערכת, ולכן אי אפשר להקריא את הקוד בטלפון. אפשר לנסות במסרון, או לפנות למנהל המערכת.",
                    "VoiceNotConfigured")),
                _ => SendResult(result, "תכף נתקשר אליך ונקריא את הקוד. הטלפון יצלצל בעוד כמה שניות.", "מספר הטלפון כבר מאומת.")
            };
        }

        [HttpPost("verify-phone")]
        [Authorize]
        public async Task<IActionResult> VerifyPhone([FromBody] VerifyCodeDto model) =>
            VerifyResult(await _userService.VerifyPhoneAsync(RequireUserId(), model.Code), "מספר הטלפון אומת בהצלחה!");

        private IActionResult SendResult(VerificationResult result, string success, string alreadyVerified) => result switch
        {
            VerificationResult.Success => Message(success),
            // סביבת פיתוח בלי ספק SMS: אומרים את האמת - ה-SMS לא נשלח, והקוד נמצא ביומן השרת
            VerificationResult.SentToLogOnly => Message(
                "סביבת פיתוח: לא הוגדר ספק SMS, ולכן ההודעה לא נשלחה לטלפון בפועל. הקוד נכתב ביומן (Console) של השרת."),
            VerificationResult.UserNotFound => Unauthorized(),
            VerificationResult.AlreadyVerified => BadRequest(Error(400, alreadyVerified)),
            VerificationResult.Cooldown => StatusCode(429, Error(429, "נשלח קוד לאחרונה. נסו שוב בעוד דקה.")),
            VerificationResult.SmsNotConfigured => StatusCode(503, Error(503,
                "שליחת מסרונים (SMS) עדיין לא הוגדרה במערכת, ולכן אי אפשר לשלוח קוד לטלפון. נא לפנות למנהל המערכת.", "SmsNotConfigured")),
            // תיקון 65: Twilio החזירה שהמספר לא מקבל מסרונים
            VerificationResult.CannotReceiveSms => BadRequest(Error(400,
                "המספר הזה לא יכול לקבל מסרונים (למשל קו נייח). אפשר לבחור \"קוד בשיחה קולית\".", "CannotReceiveSms")),
            _ => StatusCode(503, Error(503, "לא ניתן לשלוח את הקוד כרגע. נסו שוב מאוחר יותר."))
        };

        private IActionResult VerifyResult(VerificationResult result, string success) => result switch
        {
            VerificationResult.Success => Message(success),
            VerificationResult.UserNotFound => Unauthorized(),
            VerificationResult.TooManyAttempts => BadRequest(Error(400, "בוצעו יותר מדי ניסיונות שגויים. יש לבקש קוד חדש.")),
            // תיקון 65: Twilio Verify לא זמינה כרגע - לא אומרים "הקוד שגוי" כשלא באמת נבדק
            VerificationResult.SendFailed => StatusCode(503, Error(503, "לא ניתן לבדוק את הקוד כרגע. נסו שוב בעוד רגע.")),
            _ => BadRequest(Error(400, "הקוד שגוי או שפג תוקפו."))
        };

        private static Infrastructure.ApiError Error(int status, string? message, string? code = null) =>
            new() { Status = status, Message = message ?? string.Empty, Code = code };
    }
}
