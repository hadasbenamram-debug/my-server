using DAL.Classes;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IVolunteerRouteRepository : IRepository<VolunteerRoute>
    {
        // המסלול כולל כל השידוכים, התרומות (והספקים), העמותה והמתנדב
        Task<VolunteerRoute?> GetWithDetailsAsync(int routeId);

        // המסלול הפעיל של המתנדב (Planned / InProgress), אם יש
        Task<VolunteerRoute?> GetActiveForVolunteerAsync(int volunteerId);
    }
}
