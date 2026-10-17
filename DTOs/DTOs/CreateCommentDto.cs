using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // Input for POST api/comments.
    // Id, Author and CreatedAt are NOT accepted from the client - the server sets them
    // (Author = the logged-in user's name taken from the token/DB).
    // Exactly one of DonationId / MatchId must be supplied, unless ParentCommentId is given
    // (a reply always belongs to the same donation/match as its parent).
    public class CreateCommentDto
    {
        [Required(ErrorMessage = "תוכן התגובה הוא שדה חובה.")]
        [StringLength(1000, ErrorMessage = "תוכן התגובה אינו יכול לעלות על 1000 תווים.")]
        public string Content { get; set; } = string.Empty;

        public int? ParentCommentId { get; set; }

        public int? DonationId { get; set; }

        public int? MatchId { get; set; }
    }
}
