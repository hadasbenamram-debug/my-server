using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // Input for POST api/auth/register.
    // Deliberately has NO Role / IsEmailVerified / Id - those are always decided on the server.
    public class RegisterDto
    {
        [Required(ErrorMessage = "שם הוא שדה חובה.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "השם חייב להכיל בין 2 ל-100 תווים.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "כתובת אימייל היא שדה חובה.")]
        [EmailAddress(ErrorMessage = "פורמט כתובת האימייל אינו תקין.")]
        [StringLength(254, ErrorMessage = "כתובת האימייל ארוכה מדי.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "מספר טלפון הוא שדה חובה.")]
        [Phone(ErrorMessage = "מספר הטלפון אינו תקין.")]
        [StringLength(20, ErrorMessage = "מספר הטלפון יכול להכיל עד 20 תווים.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "סיסמה היא שדה חובה.")]
        [MinLength(8, ErrorMessage = "הסיסמה חייבת להכיל לפחות 8 תווים.")]
        [StringLength(100, ErrorMessage = "הסיסמה יכולה להכיל עד 100 תווים.")]
        public string Password { get; set; } = string.Empty;
    }
}
