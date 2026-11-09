using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // הודעה אחת בשיחה עם העוזר החכם.
    // Role: מי כתב אותה - user (המשתמש) או assistant (המודל).
    // את הודעת המערכת (system - ההוראות למודל) מוסיף רק השרת, ולכן הקליינט לא יכול לשלוח אותה.
    public class ChatMessageDto
    {
        [Required(ErrorMessage = "חסר סוג ההודעה.")]
        [RegularExpression("^(user|assistant)$", ErrorMessage = "סוג ההודעה חייב להיות user או assistant.")]
        public string Role { get; set; } = string.Empty;

        [Required(ErrorMessage = "תוכן ההודעה הוא שדה חובה.")]
        [MaxLength(2000, ErrorMessage = "ההודעה יכולה להכיל עד 2000 תווים.")]
        public string Text { get; set; } = string.Empty;
    }

    // מה שהקליינט שולח בכל שאלה: כל השיחה עד עכשיו (השרת לא שומר שיחות), כשההודעה האחרונה היא השאלה החדשה.
    public class ChatRequestDto
    {
        // MaxLength(30): כל ההיסטוריה נשלחת למודל בכל שאלה ועולה כסף - הקליינט שולח רק את ההודעות האחרונות
        [Required(ErrorMessage = "חסרות הודעות.")]
        [MinLength(1, ErrorMessage = "יש לשלוח לפחות הודעה אחת.")]
        [MaxLength(30, ErrorMessage = "אפשר לשלוח עד 30 הודעות בשיחה.")]
        public List<ChatMessageDto> Messages { get; set; } = new();

        // התפקיד הפעיל בקליינט (Volunteer / Supplier / Ngo / Admin) - לחשבון יכולים להיות כמה תפקידים.
        // זו רק בקשה: השרת בודק מול הפרופילים המאושרים של המשתמש, ולא נותן כלים של תפקיד שאין לו.
        [StringLength(20)]
        public string? ActiveRole { get; set; }
    }
}
