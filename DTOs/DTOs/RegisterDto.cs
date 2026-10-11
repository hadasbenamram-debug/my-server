using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ייעודי לקבצים/בקשות הרשמה של משתמש חדש
    public class RegisterDto
    {
        [Required(ErrorMessage = "שם משתמש הוא שדה חובה.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "שם המשתמש חייב להכיל בין 3 ל-50 תווים.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "כתובת אימייל היא שדה חובה.")]
        [EmailAddress(ErrorMessage = "פורמט כתובת האימייל אינו תקין.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "סיסמה היא שדה חובה.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "הסיסמה חייבת להכיל לפחות 8 תווים.")]
        public string Password { get; set; }
    }
}