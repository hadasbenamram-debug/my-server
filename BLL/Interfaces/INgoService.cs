using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface INgoService
    {
        // מנהל מקבל את כל העמותות עם כל הפרטים (NgoResponseDto).
        // מתנדב מאושר מקבל רק עמותות מאושרות עם הפרטים הדרושים למשלוח (NgoPublicDto).
        // כל משתמש אחר - ForbiddenException.
        Task<object> GetNgosForCallerAsync(CurrentUserDto currentUser);

        // מנהל או העמותה עצמה: NgoResponseDto. מתנדב מאושר: NgoPublicDto של עמותה מאושרת.
        // אחרת null (כדי לא לחשוף שהמזהה קיים).
        Task<object?> GetNgoForCallerAsync(int id, CurrentUserDto currentUser);

        Task<NgoResponseDto?> GetMineAsync(int userId);
        Task<List<AdminNgoListItemDto>> GetAdminListAsync();
        Task<NgoResponseDto> RegisterWithUserAsync(RegisterNgoDto dto);
        Task UpdateMyHoursAsync(int userId, UpdateNgoHoursDto dto);
    }
}
