using DAL.Classes;
using System;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IActionTokenRepository : IRepository<ActionToken>
    {
        // טוקן בתוקף (לא נוצל ולא פג) לפי מטרה ו-Hash
        Task<ActionToken?> FindActiveByHashAsync(ActionTokenPurpose purpose, string tokenHash, DateTime nowUtc);

        // הטוקן האחרון שנוצר למשתמש למטרה מסוימת (לצורך Cooldown ובדיקת קוד)
        Task<ActionToken?> GetLatestForUserAsync(int userId, ActionTokenPurpose purpose);

        // טוקן בתוקף של משתמש למטרה + ערך (למשל שם קובץ תמונה שהעלה)
        Task<ActionToken?> FindActiveForUserAsync(int userId, ActionTokenPurpose purpose, string targetValue, DateTime nowUtc);

        // מסמן כמנוצלים את כל הטוקנים הפעילים של המשתמש למטרה (ולישות, אם צוינה)
        Task ConsumeActiveAsync(int? userId, ActionTokenPurpose purpose, int? targetId, DateTime nowUtc);

        Task<int> DeleteExpiredAsync(DateTime cutoffUtc);
    }
}
