using AutoMapper;
using DAL;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using MatchStatus = DAL.Classes.MatchStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace BLL.Services
{
    // דוחות ונתוני דשבורד למנהל.
    public class AdminReportService : IAdminReportService
    {
        private static readonly string[] DayNames = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };

        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IApprovalRequestService _approvals;
        private readonly IDonationRepository _donationRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IMapper _mapper;

        public AdminReportService(
            IVolunteerRepository volunteerRepository,
            IApprovalRequestService approvals,
            IDonationRepository donationRepository,
            IMatchRepository matchRepository,
            IMapper mapper)
        {
            _volunteerRepository = volunteerRepository;
            _approvals = approvals;
            _donationRepository = donationRepository;
            _matchRepository = matchRepository;
            _mapper = mapper;
        }

        public async Task<SystemReportDto> GetSystemReportAsync()
        {
            var totalVolunteersCount = await _volunteerRepository.CountByApprovalStatusAsync(ApprovalStatus.Approved);

            // מתנדבים מובילים לפי ציון אמינות (5 הראשונים מבין המאושרים)
            var topVolunteers = await _volunteerRepository.GetByApprovalStatusWithUserAsync(ApprovalStatus.Approved, take: 5);

            return new SystemReportDto
            {
                TotalDonationsVolume = totalVolunteersCount * 50,
                AverageResponseTime = "4 דקות",
                SuccessRate = "95%",
                TopVolunteers = topVolunteers
                    .Select(v => new TopVolunteerDto
                    {
                        Name = v.User != null ? v.User.Name : "מתנדב",
                        DeliveriesCount = 0
                    })
                    .ToList()
            };
        }

        public async Task<DashboardStatsDto> GetDashboardStatsAsync()
        {
            return new DashboardStatsDto
            {
                OpenDonations = await _donationRepository.CountByStatusAsync(DonationStatus.Pending),
                AvailableVolunteers = await _volunteerRepository.CountAvailableAsync(),
                ActiveAssignments = await _matchRepository.CountActiveAsync(),
                NoShowAlerts = await _matchRepository.CountByStatusSinceAsync(MatchStatus.NoShow, DateTime.UtcNow.AddDays(-7))
            };
        }

        // כל בקשות ההצטרפות של מתנדבים שממתינות לאישור
        public Task<List<PendingApprovalRequestDto>> GetPendingVolunteerRequestsAsync() =>
            _approvals.GetPendingAsync(RequestedRole.Volunteer);

        // זמינות המתנדבים המאושרים - שורה לכל משבצת זמינות אמיתית (בעבר הוחזר "ראשון 08:00-16:00" לכולם)
        public async Task<List<VolunteerAvailabilitySummaryDto>> GetVolunteersAvailabilityAsync()
        {
            var volunteers = await _volunteerRepository.GetByApprovalStatusWithUserAsync(ApprovalStatus.Approved);

            return volunteers
                .SelectMany(v => (v.AvailabilitySlots ?? (ICollection<AvailabilitySlot>)new List<AvailabilitySlot>())
                    .OrderBy(s => s.Day).ThenBy(s => s.StartTime)
                    .Select(s => new VolunteerAvailabilitySummaryDto
                    {
                        VolunteerId = v.Id,
                        VolunteerName = v.User != null ? v.User.Name : "מתנדב",
                        Day = DayNames[(int)s.Day],
                        TimeSlot = $"{s.StartTime:hh\\:mm} - {s.EndTime:hh\\:mm}"
                    }))
                .ToList();
        }

        // מוניטור פעילות בשטח: מסלול פעיל לכל מתנדב, כולל המיקום האחרון שלו
        public async Task<List<LiveMissionDto>> GetLiveMonitorAsync()
        {
            var active = await _matchRepository.GetActiveMatchesAsync();

            return active
                .GroupBy(m => m.VolunteerRouteId ?? -m.Id)
                .Select(g =>
                {
                    var first = g.OrderBy(m => m.EstimatedArrival ?? DateTime.MaxValue).First();
                    var volunteer = first.Volunteer;
                    return new LiveMissionDto
                    {
                        Id = first.VolunteerRouteId ?? first.Id,
                        VolunteerId = first.VolunteerId,
                        VolunteerName = volunteer?.User?.Name ?? $"מתנדב #{first.VolunteerId}",
                        Destination = first.Ngo != null ? $"{first.Ngo.NgoName} - {first.Ngo.Address}" : "",
                        SupplierName = string.Join(", ", g.Select(m => m.Donation?.Supplier?.BusinessName).Where(n => !string.IsNullOrEmpty(n)).Distinct()),
                        Status = g.Any(m => m.Status == MatchStatus.InTransit) ? "InTransit"
                               : g.Any(m => m.Status == MatchStatus.InPickup) ? "InPickup"
                               : g.All(m => m.Status == MatchStatus.AwaitingVolunteerConfirmation) ? "AwaitingVolunteerConfirmation"
                               : "Confirmed",
                        StopsCount = g.Count(),
                        Latitude = volunteer?.LocationUpdatedAt != null ? volunteer.CurrentLatitude : null,
                        Longitude = volunteer?.LocationUpdatedAt != null ? volunteer.CurrentLongitude : null,
                        LocationUpdatedAt = volunteer?.LocationUpdatedAt
                    };
                })
                .ToList();
        }
    }
}
