using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IVolunteerRepository : IRepository<Volunteer>
    {
        // מתנדבים שאפשר לשבץ: מאושרים, זמינים, ורישיון הנהיגה שלהם לא פג (todayUtc)
        Task<List<Volunteer>> GetAvailableVolunteersAsync(DateTime todayUtc);

        Task<Volunteer?> GetByUserIdAsync(int userId);

        // מתנדב עם המזהה הזה *וגם* של המשתמש הזה (null = לא קיים או לא שלו)
        Task<Volunteer?> GetOwnedAsync(int id, int userId);
        Task<List<Volunteer>> GetAllWithUserAsync();

        // מתנדבים בסטטוס אישור מסוים (כולל פרטי המשתמש). take = הגבלת מספר התוצאות.
        Task<List<Volunteer>> GetByApprovalStatusWithUserAsync(ApprovalStatus status, int? take = null);
        Task<int> CountByApprovalStatusAsync(ApprovalStatus status);
        Task<int> CountAvailableAsync();
    }
}
