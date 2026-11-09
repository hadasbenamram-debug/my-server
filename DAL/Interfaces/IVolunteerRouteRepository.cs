using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IVolunteerRouteRepository : IRepository<VolunteerRoute>
    {
        // המסלול כולל כל השידוכים, התרומות (והספקים), העמותה והמתנדב
        Task<VolunteerRoute?> GetWithDetailsAsync(int routeId);

        // המסלול הפעיל של המתנדב (Planned / InProgress), אם יש
        Task<VolunteerRoute?> GetActiveForVolunteerAsync(int volunteerId);

        // רק המזהה של המסלול שהמתנדב נמצא בו עכשיו בדרך (InProgress) - שאילתה קלה לכל עדכון מיקום
        Task<int?> GetInProgressRouteIdAsync(int volunteerId);

        // כל המסלולים שמתנדבים נמצאים בהם עכשיו בדרך (InProgress), עם אותם פרטים כמו GetWithDetailsAsync - למפה החיה של המנהל
        Task<List<VolunteerRoute>> GetInProgressWithDetailsAsync();
    }
}
