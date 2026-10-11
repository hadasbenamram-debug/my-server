using BLL.Algorithms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    public class CommentNode
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public string AuthorName { get; set; }
        public List<CommentNode> Replies { get; set; } = new List<CommentNode>();
    }

    /// <summary>
    /// DTO פשוט לייצוג תגובה גולמית כפי שמגיעה מה-DAL (טבלה שטוחה עם ParentId).
    /// </summary>
    public class CommentDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Text { get; set; }
        public string AuthorName { get; set; }
    }

    /// <summary>
    /// בונה עץ תגובות מקונן (תגובה על תגובה, בעומק בלתי מוגבל) מתוך רשימה שטוחה.
    ///
    /// תוקן: הגרסה הקודמת הריצה .Where(c => c.ParentId == parentId) על כל הרשימה
    /// השטוחה בכל צומת בעץ - במקרה הגרוע (עץ "רדוד ורחב") זה O(n²). כעת בונים
    /// ParentId -> List&lt;CommentDto&gt; לוקאפ אחד בתחילת הבנייה (O(n)), וכל צומת
    /// שולף את הילדים שלו ב-O(1) בממוצע - הבנייה כולה יורדת ל-O(n).
    /// </summary>
    public class CommentTreeService
    {
        /// <summary>
        /// הגנה טכנית מכוונת (Safeguard): הגבלת עומק עץ התגובות למקסימום 100 רמות 
        /// כברירת מחדל מכוונת, למניעת חריגות זיכרון (Stack Overflow) או עומס מיותר על השרת.
        /// </summary>
        private const int MaxDepth = 100;

        public List<CommentNode> BuildCommentTree(IEnumerable<CommentDto> flatComments, int? parentId = null)
        {
            var lookup = (flatComments ?? Enumerable.Empty<CommentDto>())
                .ToLookup(c => c.ParentId);

            return BuildRecursive(lookup, parentId, depth: 0);
        }

        private List<CommentNode> BuildRecursive(ILookup<int?, CommentDto> lookup, int? parentId, int depth)
        {
            if (depth >= MaxDepth)
                return new List<CommentNode>(); // עצירה בטוחה - לא אמור לקרות בנתונים תקינים

            return lookup[parentId]
                .Select(c => new CommentNode
                {
                    Id = c.Id,
                    Text = c.Text,
                    AuthorName = c.AuthorName,
                    Replies = BuildRecursive(lookup, c.Id, depth + 1)
                })
                .ToList();
        }
    }
}
