using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IRegionRepository : IRepository<Region>
    {
        Task<IEnumerable<Region>> GetAllRegionsAsync();
    }
}