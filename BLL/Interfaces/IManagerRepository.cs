using DAL.Classes;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IManagerRepository : IRepository<Manager>
    {
        Task<Manager> GetByUserIdAsync(int userId);
    }
}