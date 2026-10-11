using DAL.Classes;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<Supplier> GetByUserIdAsync(int userId);
    }
}