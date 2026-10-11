using DAL.Classes;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface INgoRepository : IRepository<Ngo>
    {
        Task<Ngo> GetByUserIdAsync(int userId);
    }
}