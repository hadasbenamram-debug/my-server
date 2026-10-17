using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface INotificationRepository : IRepository<Notification>
    {
        // האם נשלחה למשתמש התראה עם אותו נושא מאז זמן מסוים (לא כולל התראות שנכשלו)
        Task<bool> HasRecentNonFailedAsync(int userId, string subject, DateTime sinceUtc);

        // "תפיסה" אטומית של הודעה לשליחה (Pending -> Sending), כדי ששני שרתים לא ישלחו אותה פעמיים
        Task<bool> TryClaimForSendingAsync(int id, DateTime nowUtc);

        // ההודעה במעקב, עם הערכים העדכניים מהמסד
        Task<Notification?> GetFreshAsync(int id);

        // הודעות מייל/SMS שהגיע זמן לשלוח
        Task<List<int>> GetDueIdsAsync(DateTime nowUtc, int take);

        // הודעות שנתקעו במצב Sending (למשל השרת נפל באמצע) חוזרות לתור
        Task<int> ResetStuckSendingAsync(DateTime olderThanUtc);

        // מרכז ההתראות של המשתמש (InApp)
        Task<PagedResult<Notification>> GetInAppForUserAsync(int userId, int skip, int take);
        Task<int> CountUnreadInAppAsync(int userId);
        Task MarkInAppReadAsync(int userId, IReadOnlyCollection<int>? ids, DateTime nowUtc);
    }
}
