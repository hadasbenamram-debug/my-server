using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // פנייה מטופס "צור קשר" (גם מהאתר הציבורי וגם מתוך האפליקציה)
    public class ContactMessageDto
    {
        [Required(ErrorMessage = "יש להזין שם.")]
        [StringLength(100, ErrorMessage = "השם יכול להכיל עד 100 תווים.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין אימייל.")]
        [EmailAddress(ErrorMessage = "כתובת האימייל אינה תקינה.")]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        // אופציונלי
        [StringLength(20, ErrorMessage = "מספר הטלפון ארוך מדי.")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "יש להזין נושא.")]
        [StringLength(150, ErrorMessage = "הנושא יכול להכיל עד 150 תווים.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין תוכן הודעה.")]
        [StringLength(1000, ErrorMessage = "ההודעה יכולה להכיל עד 1000 תווים.")]
        public string Message { get; set; } = string.Empty;
    }
}
