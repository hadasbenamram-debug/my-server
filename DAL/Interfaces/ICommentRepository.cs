using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ICommentRepository : IRepository<Comment>
    {
        // כל התגובות, לקריאה בלבד
        Task<List<Comment>> GetAllReadOnlyAsync();

        // תגובות על תרומות/שידוכים שהמתנדב, העמותה או הספק משתתפים בהם.
        // מזהה -1 אף פעם לא תואם רשומה אמיתית, כך שפרופיל חסר פשוט לא מחזיר כלום.
        Task<List<Comment>> GetForParticipantAsync(int volunteerId, int ngoId, int supplierId);

        // כל התגובות על אותה תרומה / אותו שידוך (לחישוב תת-עץ למחיקה)
        Task<List<Comment>> GetForTargetAsync(int? donationId, int? matchId);

        Task DeleteRangeAsync(IEnumerable<Comment> comments);
    }
}
