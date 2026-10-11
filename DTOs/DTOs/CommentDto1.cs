using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTOs.DTOs
{
    public class CommentDto1
    {
        public int Id { get; set; }

        [StringLength(100, ErrorMessage = "שם הכותב יכול להכיל עד 100 תווים.")]
        public string Author { get; set; }

        [Required(ErrorMessage = "תוכן התגובה הוא שדה חובה.")]
        [StringLength(1000, ErrorMessage = "תוכן התגובה אינו יכול לעלות על 1000 תווים.")]
        public string Content { get; set; }

        public DateTime CreatedAt { get; set; }
        public int? ParentCommentId { get; set; }
        public List<CommentDto1> Replies { get; set; } = new List<CommentDto1>();
        public int? DonationId { get; set; }
        public int? MatchId { get; set; }
    }
}