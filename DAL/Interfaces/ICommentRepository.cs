using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ICommentRepository : IRepository<Comment>
    {
        // כל התגובות, לקריאה בלבד (כולל התרומה והספק שלה - לתצוגה)
        Task<List<Comment>> GetAllReadOnlyAsync();

        // תגובות על תרומות שהמתנדב, העמותה או הספק קשורים אליהן.
        // מזהה -1 אף פעם לא תואם רשומה אמיתית, כך שפרופיל חסר פשוט לא מחזיר כלום.
        Task<List<Comment>> GetForParticipantAsync(int volunteerId, int ngoId, int supplierId);

        // תיקון 18: כל התגובות על אותה תרומה (לחישוב תת-עץ למחיקה)
        Task<List<Comment>> GetForDonationAsync(int donationId);

        Task DeleteRangeAsync(IEnumerable<Comment> comments);
    }
}
