using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // הפרופיל של המשתמש המחובר (GET api/users/me)
    public class MyProfileDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsEmailVerified { get; set; }
        public bool IsPhoneVerified { get; set; }
        public bool MustChangePassword { get; set; }

        // תיקון 10: כל התפקידים של החשבון עם הסטטוס של כל אחד (Approved / Pending / Rejected)
        public List<AccountRoleDto> Roles { get; set; } = new();

        // אפשר להוסיף לחשבון תפקיד נוסף (מתנדב / ספק / עמותה). מנהל - לא.
        public bool CanAddRoles { get; set; }

        // תיקון 32: תעודת זהות שכבר שמורה בחשבון, מוסתרת ("•••••6789") - מפרופיל המתנדב, או ממספר העוסק של הספק
        // כשהוא מספר זהות של אדם (עוסק מורשה / פטור). בהוספת תפקיד השרת ממלא אותה בעצמו. null = אין.
        public string? KnownIdNumberMasked { get; set; }
    }

    public class AccountRoleDto
    {
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    // עדכון פרופיל (PUT api/users/me). האימייל לא מתעדכן כאן - הוא מזהה ההתחברות.
    public class UpdateMyProfileDto
    {
        [Required(ErrorMessage = "יש להזין שם.")]
        [StringLength(100, ErrorMessage = "השם יכול להכיל עד 100 תווים.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין מספר טלפון.")]
        [Phone(ErrorMessage = "מספר הטלפון אינו תקין.")]
        [StringLength(20, ErrorMessage = "מספר הטלפון יכול להכיל עד 20 תווים.")]
        public string Phone { get; set; } = string.Empty;
    }
}
