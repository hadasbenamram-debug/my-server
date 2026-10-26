using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // Input for POST api/comments.
    // Id, Author and CreatedAt are NOT accepted from the client - the server sets them
    // (Author = the logged-in user's name taken from the token/DB).
    // תיקון 18: כל תגובה היא על תרומה מסוימת - DonationId חובה, אלא אם זו תשובה לתגובה (ParentCommentId),
    // ואז התגובה שייכת לתרומה של התגובה שאליה עונים. אין יותר תגובות על שיבוץ (MatchId).
    public class CreateCommentDto
    {
        [Required(ErrorMessage = "תוכן התגובה הוא שדה חובה.")]
        [StringLength(1000, ErrorMessage = "תוכן התגובה אינו יכול לעלות על 1000 תווים.")]
        public string Content { get; set; } = string.Empty;

        public int? ParentCommentId { get; set; }

        public int? DonationId { get; set; }
    }
}
