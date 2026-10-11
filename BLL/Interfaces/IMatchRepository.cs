using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IMatchRepository : IRepository<Match>
    {
        Task<IEnumerable<Match>> GetActiveMatchesAsync();

        // האם למתנדב יש שיבוץ פעיל (Confirmed / InPickup / InTransit) שאינו השידוך הנוכחי.
        // matches של אותו מסלול (VolunteerRouteId זהה) נחשבים כשיבוץ אחד ולכן אינם נספרים.
        Task<bool> HasActiveConfirmedMatchAsync(int volunteerId, int excludeMatchId, int? excludeRouteId = null);
    }
}