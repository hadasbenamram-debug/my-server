using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IDeliveryLogRepository : IRepository<DeliveryLog>
    {
        // לוגים של המסלולים ששייכים למתנדב
        Task<List<DeliveryLog>> GetByVolunteerIdAsync(int volunteerId);

        Task<PagedResult<DeliveryLog>> GetPageAsync(int skip, int take);

        // היומן של מסלול לפי סדר הזמן (מהישן לחדש). במסלול ארוך - רק maxRows הרישומים האחרונים.
        Task<List<DeliveryLog>> GetForRouteAsync(int routeId, int maxRows);

        // עדכון המיקום האחרון שנרשם במסלול (null אם עוד אין)
        Task<DeliveryLog?> GetLastLocationAsync(int routeId);

        // הוספה שלא מפילה את הפעולה שקראה לה: אם השמירה נכשלה - הרישום יוצא מהמעקב ומוחזר false
        Task<bool> TryAddAsync(DeliveryLog log);

        // ניקוי: עדכוני מיקום ישנים (נתוני GPS של המתנדבים לא נשמרים לנצח). האירועים עצמם נשארים.
        Task<int> DeleteLocationsOlderThanAsync(DateTime cutoffUtc);
    }
}
