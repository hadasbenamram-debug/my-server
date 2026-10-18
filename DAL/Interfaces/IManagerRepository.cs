using DAL.Classes;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IManagerRepository : IRepository<Manager>
    {
        Task<Manager?> GetByUserIdAsync(int userId);
    }
}
