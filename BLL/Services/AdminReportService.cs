using AutoMapper;
using DAL;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        // תיקון 9: ביום שבת יש רק זמינות של מוצאי שבת
        private static readonly string[] DayNames = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "מוצאי שבת" };

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

        // תיקון 17: בעבר "מובילי ביצועי מתנדבים" הציג את 5 המתנדבים עם ציון האמינות הגבוה, וכולם עם 0 משלוחים
        // (המספר היה קבוע בקוד). גם שאר המספרים בדוח היו קבועים: "4 דקות", "95%", ומספר המתנדבים כפול 50.
        // עכשיו הכול מחושב מהנתונים בפועל.
        private const int TopVolunteersCount = 5;
        private const string NoData = "—";

        public async Task<SystemReportDto> GetSystemReportAsync()
        {
            var topVolunteers = await _matchRepository.GetTopVolunteersByDeliveriesAsync(TopVolunteersCount);
            var responseTimes = await _matchRepository.GetOfferResponseTimesAsync();
            var deliveredKg = await _donationRepository.SumDeliveredWeightKgAsync();

            // אחוז הצלחה: מתוך התרומות שהסתיימו - כמה הגיעו לעמותה (ולא פג תוקפן). תרומה שבוטלה לא נספרת.
            int delivered = await _donationRepository.CountByStatusAsync(DonationStatus.Delivered)
                          + await _donationRepository.CountByStatusAsync(DonationStatus.Completed);
            int expired = await _donationRepository.CountByStatusAsync(DonationStatus.Expired);

            return new SystemReportDto
            {
                TotalDonationsVolume = (int)Math.Round(deliveredKg),
                AverageResponseTime = responseTimes.Count == 0
                    ? NoData
                    : responseTimes.Average(t => t.TotalMinutes).ToString("0.0", CultureInfo.InvariantCulture),
                SuccessRate = delivered + expired == 0
                    ? NoData
                    : $"{(int)Math.Round(100.0 * delivered / (delivered + expired))}%",
                TopVolunteers = topVolunteers
                    .Select(v => new TopVolunteerDto
                    {
                        Name = v.VolunteerName ?? $"מתנדב #{v.VolunteerId}",
                        DeliveriesCount = v.Deliveries
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
            _approvals.GetPendingAsync(FoodRescue.DTOs.RequestedRole.Volunteer);

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
