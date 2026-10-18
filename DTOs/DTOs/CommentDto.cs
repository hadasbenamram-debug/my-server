using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // מחלקה אחת לתגובה (במקום CommentDto, CommentDto1 ו-CommentNode שהיו קודם).
    // משמשת גם לרשימה השטוחה (מנהל) וגם לעץ התגובות (Replies ממולא רק בעץ).
    public class CommentDto
    {
        public int Id { get; set; }
        public int? ParentCommentId { get; set; }
        public int? DonationId { get; set; }
        public int? MatchId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<CommentDto> Replies { get; set; } = new();
    }
}
