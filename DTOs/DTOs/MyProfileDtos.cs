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
