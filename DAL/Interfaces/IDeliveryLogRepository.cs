using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IDeliveryLogRepository : IRepository<DeliveryLog>
    {
        // לוגים של המסלולים ששייכים למתנדב
        Task<List<DeliveryLog>> GetByVolunteerIdAsync(int volunteerId);

        Task<PagedResult<DeliveryLog>> GetPageAsync(int skip, int take);
    }
}
