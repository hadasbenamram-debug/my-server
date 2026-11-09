using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // המעקב החי אחרי מסלול - נבנה מיומן המשלוח (DeliveryLogs)
    public interface IDeliveryTrackingService
    {
        // מצב המסלול עכשיו: התחנות, מסלול הנסיעה בפועל, המיקום האחרון, האירועים וזמן ההגעה המשוער לעמותה.
        // null = המסלול לא קיים, או שהמשתמש לא קשור אליו (מותר: מנהל, המתנדב, העמותה, ספק שתרומה שלו במסלול).
        Task<RouteTrackingDto?> GetRouteTrackingAsync(int routeId, CurrentUserDto currentUser);

        // המפה החיה של המנהל: המעקב של כל המסלולים שבדרך עכשיו (בלי זמן הגעה מ-Google). לא מנהל - ForbiddenException.
        Task<List<RouteTrackingDto>> GetLiveRoutesAsync(CurrentUserDto currentUser);
    }
}
