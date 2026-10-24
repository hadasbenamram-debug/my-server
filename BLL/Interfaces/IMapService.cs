using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IMapService
    {
        Task<List<MapEntityDto>> GetAllEntitiesAsync();

        // תמונת מפה של כל הישויות. hiddenTypes - הסוגים שלא יוצגו: supplier / ngo / volunteer
        Task<MapImageDto> GetAllEntitiesImageAsync(IReadOnlyCollection<string> hiddenTypes);

        // תמונת מפה של מסלול. null אם המסלול לא קיים או שהמשתמש לא קשור אליו
        Task<MapImageDto?> GetRouteImageAsync(int routeId, CurrentUserDto currentUser);
    }
}
