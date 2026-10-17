using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface INgoRepository : IRepository<Ngo>
    {
        Task<Ngo?> GetByUserIdAsync(int userId);
        Task<List<Ngo>> GetAllWithUserAsync();

        // עמותות מאושרות בלבד (המנוע לא משבץ לעמותה שלא אושרה)
        Task<List<Ngo>> GetApprovedAsync();

        Task<bool> AnyInRegionAsync(int regionId);
    }
}
