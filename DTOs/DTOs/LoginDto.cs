using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // Login intentionally does not enforce the password policy ([MinLength]) or [EmailAddress]:
    // doing so would reveal the policy and would lock out accounts created before it existed.
    // The upper bounds protect the PBKDF2 hashing from absurdly large inputs.
    public class LoginDto
    {
        [Required(ErrorMessage = "יש להזין כתובת אימייל.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין סיסמה.")]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;
    }
}
