using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IAdminReportService
    {
        Task<SystemReportDto> GetSystemReportAsync();
        Task<DashboardStatsDto> GetDashboardStatsAsync();
        Task<List<PendingApprovalRequestDto>> GetPendingVolunteerRequestsAsync();
        Task<List<VolunteerAvailabilitySummaryDto>> GetVolunteersAvailabilityAsync();
        Task<List<LiveMissionDto>> GetLiveMonitorAsync();
    }
}
