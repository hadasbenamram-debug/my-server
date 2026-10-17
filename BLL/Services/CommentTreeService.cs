using DAL.Classes;
using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// בונה עץ תגובות מקונן (Recursive Comment Tree): תגובה ראשית מצביעה על null, תת-תגובה על האב שלה,
    /// עומק לא מוגבל. בנייה ב-O(n) בעזרת Lookup של ParentId, ומעבר רקורסיבי לבניית העץ ולספירה.
    /// </summary>
    public class CommentTreeService
    {
        // הגנה טכנית: עומק רקורסיה מקסימלי, למניעת Stack Overflow בנתונים פגומים (מעגל)
        private const int MaxDepth = 100;

        public List<CommentDto> BuildCommentTree(IEnumerable<Comment> flatComments, int? parentId = null)
        {
            var list = (flatComments ?? Enumerable.Empty<Comment>()).ToList();
            var ids = list.Select(c => c.Id).ToHashSet();

            // תגובה שהאב שלה לא נגיש למשתמש (או נמחק) מוצגת כתגובה ראשית
            var lookup = list.ToLookup(c => c.ParentCommentId.HasValue && ids.Contains(c.ParentCommentId.Value) ? c.ParentCommentId : null);

            return BuildRecursive(lookup, parentId, depth: 0);
        }

        private static List<CommentDto> BuildRecursive(ILookup<int?, Comment> lookup, int? parentId, int depth)
        {
            if (depth >= MaxDepth)
                return new List<CommentDto>();

            return lookup[parentId]
                .OrderBy(c => c.CreatedAt)
                .Select(c =>
                {
                    var node = ToDto(c);
                    node.Replies = BuildRecursive(lookup, c.Id, depth + 1);
                    return node;
                })
                .ToList();
        }

        // ספירה רקורסיבית של כל התגובות בעץ
        public static int CountRecursive(IEnumerable<CommentDto> nodes) =>
            nodes.Sum(n => 1 + CountRecursive(n.Replies));

        // כל הצאצאים של תגובה (לצורך מחיקה)
        public static List<Comment> CollectSubtree(IEnumerable<Comment> sameTarget, int rootId)
        {
            var byParent = sameTarget.ToLookup(c => c.ParentCommentId);
            var result = new List<Comment>();
            void Walk(int id, int depth)
            {
                if (depth >= MaxDepth) return;
                foreach (var child in byParent[id])
                {
                    result.Add(child);
                    Walk(child.Id, depth + 1);
                }
            }
            Walk(rootId, 0);
            return result;
        }

        public static CommentDto ToDto(Comment c) => new CommentDto
        {
            Id = c.Id,
            ParentCommentId = c.ParentCommentId,
            DonationId = c.DonationId,
            MatchId = c.MatchId,
            AuthorName = c.Author,
            Text = c.Content,
            CreatedAt = c.CreatedAt
        };
    }
}
