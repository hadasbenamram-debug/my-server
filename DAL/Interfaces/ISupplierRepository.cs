using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<Supplier?> GetByUserIdAsync(int userId);
        Task<List<Supplier>> GetAllWithUserAsync();
    }
}
