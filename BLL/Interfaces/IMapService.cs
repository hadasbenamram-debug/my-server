using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IMapService
    {
        Task<List<MapEntityDto>> GetAllEntitiesAsync();
    }
}
