using DAL.Interfaces;
using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IMatchService
    {
        Task<PagedResult<MatchResponseDto>> GetPageAsync(PageRequest page, MatchStatus? status);

        // מחזיר null אם השידוך לא קיים או שהמשתמש לא משתתף בו
        Task<MatchResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser);

        // השידוכים הפעילים שהמשתמש משתתף בהם (מנהל - כולם)
        Task<List<MatchResponseDto>> GetActiveAsync(CurrentUserDto currentUser);

        // הפעלה ידנית של אותו מנוע שרץ כל שעה
        Task<EngineRunResultDto> RunEngineAsync();
        Task<EngineStatusDto> GetEngineStatusAsync();

        // שיבוץ ידני של תרומה (דרך אותו מנוע). null אם התרומה לא קיימת.
        Task<AssignDonationResultDto?> AssignDonationAsync(int donationId);

        // אישור / דחייה - לכל המסלול שהשידוך שייך אליו
        Task<RouteDto> AcceptAsync(int id, CurrentUserDto currentUser);
        Task DeclineAsync(int id, CurrentUserDto currentUser);

        // ביצוע המסלול
        Task<RouteDto> StartRouteAsync(int routeId, CurrentUserDto currentUser);
        Task PickUpAsync(int id, CurrentUserDto currentUser);
        Task CompleteAsync(int id, CurrentUserDto currentUser);

        // No-Show (מנהל) - שיבוץ מחדש מיידי
        Task<int> MarkNoShowAsync(int id, string? reason);

        Task<RouteDto?> GetRouteAsync(int routeId, CurrentUserDto currentUser);
        Task<RouteDto?> GetMyActiveRouteAsync(CurrentUserDto currentUser);

        // קישור מהמייל (בלי התחברות): טוקן חד-פעמי שמוגבל למסלול אחד
        Task<RouteDto?> GetRouteByLinkAsync(string token);
        Task<RouteDto> RespondByLinkAsync(RespondByLinkDto dto);
    }
}
