using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "יש לספק כתובת אימייל.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "יש לספק טוקן.")]
        [StringLength(200)]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש לספק סיסמה חדשה.")]
        [MinLength(8, ErrorMessage = "הסיסמה חייבת להכיל לפחות 8 תווים.")]
        [StringLength(100, ErrorMessage = "הסיסמה יכולה להכיל עד 100 תווים.")]
        public string NewPassword { get; set; } = string.Empty;
    }

    // החלפת סיסמה ע"י משתמש מחובר (מאמת את הסיסמה הנוכחית)
    public class ChangePasswordDto
    {
        [Required(ErrorMessage = "יש להזין את הסיסמה הנוכחית.")]
        [StringLength(100)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין סיסמה חדשה.")]
        [MinLength(8, ErrorMessage = "הסיסמה חייבת להכיל לפחות 8 תווים.")]
        [StringLength(100, ErrorMessage = "הסיסמה יכולה להכיל עד 100 תווים.")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class VerifyCodeDto
    {
        [Required(ErrorMessage = "יש לספק קוד אימות.")]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;
    }
}
