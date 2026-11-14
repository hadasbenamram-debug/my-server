using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // תיקון 75: "למה המנוע החליט כך" - ההסברים שנשמרו למסלולים (למנהל)
    public interface IRouteDecisionService
    {
        /// <summary>ההחלטות האחרונות (המסלולים האחרונים שנשמר להם הסבר)</summary>
        Task<List<RouteDecisionSummaryDto>> GetRecentAsync(int take);

        /// <summary>ההסבר המלא של מסלול. null = אין מסלול כזה, או שהוא נוצר לפני שנשמרו הסברים.</summary>
        Task<RouteDecisionDto?> GetAsync(int routeId);

        /// <summary>המסלול האחרון של תרומה שיש לו הסבר. null = אין.</summary>
        Task<int?> FindRouteForDonationAsync(int donationId);
    }
}
