using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IMatchService
    {
        Task<PagedResponse<MatchResponseDto>> GetPageAsync(PageRequest page, MatchStatus? status);

        // מחזיר null אם השידוך לא קיים או שהמשתמש לא משתתף בו
        Task<MatchResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser);

        // השידוכים הפעילים שהמשתמש משתתף בהם (מנהל - כולם)
        Task<List<MatchResponseDto>> GetActiveAsync(CurrentUserDto currentUser);

        // תיקון 16: היסטוריית השיבוצים של המתנדב המחובר - כולל שיבוצים שהסתיימו
        Task<List<MatchResponseDto>> GetMyVolunteerHistoryAsync(CurrentUserDto currentUser);

        // הפעלה ידנית של אותו מנוע שרץ כל שעה
        Task<EngineRunResultDto> RunEngineAsync();
        Task<EngineStatusDto> GetEngineStatusAsync();

        // שיבוץ ידני של תרומה (דרך אותו מנוע). null אם התרומה לא קיימת.
        Task<AssignDonationResultDto?> AssignDonationAsync(int donationId);

        // אישור / דחייה - לכל המסלול שהשידוך שייך אליו.
        // תיקון 12: מקרים צפויים (זמן התגובה עבר, כבר נענה...) חוזרים כתוצאה ולא כחריגה
        Task<RouteResponseOutcome> AcceptAsync(int id, CurrentUserDto currentUser);
        Task<RouteResponseResult> DeclineAsync(int id, CurrentUserDto currentUser);

        // ביצוע המסלול
        Task<RouteDto> StartRouteAsync(int routeId, CurrentUserDto currentUser);
        Task PickUpAsync(int id, CurrentUserDto currentUser);
        Task CompleteAsync(int id, CurrentUserDto currentUser);

        // No-Show (מנהל) - שיבוץ מחדש מיידי
        Task<int> MarkNoShowAsync(int id, string? reason);

        Task<RouteDto?> GetRouteAsync(int routeId, CurrentUserDto currentUser);
        Task<RouteDto?> GetMyActiveRouteAsync(CurrentUserDto currentUser);

        // כל המסלולים שבדרך עכשיו (InProgress) - מנהל בלבד, למפה החיה של כל המסלולים
        Task<List<RouteDto>> GetLiveRoutesAsync(CurrentUserDto currentUser);

        // קישור מהמייל (בלי התחברות): טוקן חד-פעמי שמוגבל למסלול אחד
        Task<RouteDto?> GetRouteByLinkAsync(string token);
        Task<RespondByLinkOutcome> RespondByLinkAsync(RespondByLinkDto dto);
    }

    // תשובה לבקשת התנדבות מהקישור. VolunteerUserId - כדי שאחרי אישור המתנדב ייכנס ישר למסלול (תיקון 11).
    // Route = null כשהקישור כבר לא בתוקף (Result = LinkInvalid).
    public sealed record RespondByLinkOutcome(RouteResponseResult Result, RouteDto? Route, int VolunteerUserId);

    // תיקון 12: תוצאת אישור / דחייה של בקשת התנדבות. בעבר המקרים האלה זרקו חריגות (ArgumentException /
    // ConflictException / NotFoundException), ומתנדב שנכנס אחרי 5 הדקות "הפיל" את השרת בהרצה מ-Visual Studio.
    public enum RouteResponseResult
    {
        Done,             // אושר / נדחה
        NotAwaiting,      // הבקשה כבר לא ממתינה לתגובה (אושרה / נדחתה / הועברה למתנדב אחר)
        Expired,          // זמן התגובה הסתיים
        LicenseInvalid,   // רישיון הנהיגה פג בין ההצעה לאישור - הבקשה הועברה למתנדב אחר
        VolunteerBusy,    // למתנדב כבר יש מסלול פעיל אחר
        LinkInvalid       // הקישור מהמייל / SMS כבר לא בתוקף
    }

    public sealed record RouteResponseOutcome(RouteResponseResult Result, RouteDto? Route);
}
