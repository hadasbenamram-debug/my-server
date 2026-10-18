using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IRegionService
    {
        Task<IEnumerable<RegionResponseDto>> GetAllAsync();
        Task<RegionResponseDto?> GetByIdAsync(int id);
        Task<RegionResponseDto> AddAsync(CreateRegionDto dto);
        Task DeleteAsync(int id);
    }
}
