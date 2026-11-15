using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // תיקון 77: DELETE api/users/me - מחיקת החשבון של המשתמש המחובר.
    // הסיסמה הנוכחית נדרשת, כדי שמי שמצא מחשב פתוח לא יוכל למחוק את החשבון בלחיצה אחת.
    public class DeleteAccountDto
    {
        [Required(ErrorMessage = "יש להזין את הסיסמה כדי למחוק את החשבון.")]
        [StringLength(128)]
        public string Password { get; set; } = string.Empty;
    }
}
