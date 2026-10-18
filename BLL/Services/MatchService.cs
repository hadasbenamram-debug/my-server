using AutoMapper;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Security;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MatchStatus = DAL.Classes.MatchStatus;

namespace BLL.Services
{
    public class MatchService : IMatchService
    {
        private const string MatchNotFound = "השידוך לא נמצא.";

        private readonly IMatchRepository _matchRepository;
        private readonly IDonationRepository _donationRepository;
        private readonly IVolunteerRouteRepository _routeRepository;
        private readonly DonationMatchingBatchService _engine;
        private readonly RouteWorkflowService _workflow;
        private readonly EngineRunState _runState;
        private readonly CallerProfileService _caller;
        private readonly IActionTokenService _actionTokens;
        private readonly IMatchingPolicyProvider _policy;
        private readonly IMapper _mapper;
        private readonly ILogger<MatchService> _logger;

        public MatchService(
            IMatchRepository matchRepository,
            IDonationRepository donationRepository,
            IVolunteerRouteRepository routeRepository,
            DonationMatchingBatchService engine,
            RouteWorkflowService workflow,
            EngineRunState runState,
            CallerProfileService caller,
            IActionTokenService actionTokens,
            IMatchingPolicyProvider policy,
            IMapper mapper,
            ILogger<MatchService> logger)
        {
            _matchRepository = matchRepository;
            _donationRepository = donationRepository;
            _routeRepository = routeRepository;
            _engine = engine;
            _workflow = workflow;
            _runState = runState;
            _caller = caller;
            _actionTokens = actionTokens;
            _policy = policy;
            _mapper = mapper;
            _logger = logger;
        }

        // =========================================================
        // קריאה
        // =========================================================

        public async Task<PagedResponse<MatchResponseDto>> GetPageAsync(PageRequest page, FoodRescue.DTOs.MatchStatus? status)
        {
            var result = await _matchRepository.GetPageAsync(page.Skip, page.PageSize, status.HasValue ? (MatchStatus)(int)status.Value : null);
            return new PagedResponse<MatchResponseDto>
            {
                Items = _mapper.Map<List<MatchResponseDto>>(result.Items),
                TotalCount = result.TotalCount
            };
        }

        public async Task<MatchResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser)
        {
            var match = await _matchRepository.GetByIdWithDetailsAsync(id);
            if (match == null)
                return null;

            var caller = await _caller.GetAsync(currentUser);

            // בדיקת בעלות: null (= "לא נמצא") כדי לא לחשוף שהשידוך קיים.
            if (!IsParticipant(caller, match))
                return null;

            return _mapper.Map<MatchResponseDto>(match);
        }

        public async Task<List<MatchResponseDto>> GetActiveAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);

            // תוקן: בעבר נטענו כל השידוכים הפעילים לזיכרון וסוננו שם. עכשיו הסינון נעשה במסד.
            var matches = caller.IsAdmin
                ? await _matchRepository.GetActiveMatchesAsync()
                : await _matchRepository.GetActiveForParticipantAsync(
                    caller.Volunteer?.Id ?? -1, caller.Ngo?.Id ?? -1, caller.Supplier?.Id ?? -1);

            return _mapper.Map<List<MatchResponseDto>>(matches);
        }

        // =========================================================
        // המנוע
        // =========================================================

        // תוקן: "הפעל מנוע" בנה מסלולים, כתב ללוג ולא שמר כלום (התחיל מנקודה 0,0 ובחר תמיד את העמותה
        // הראשונה). עכשיו זו בדיוק אותה ריצה של המנוע השעתי.
        public async Task<EngineRunResultDto> RunEngineAsync()
        {
            _logger.LogInformation("Manual run-engine started by administrator.");
            var run = await _engine.RunBatchAsync("Manual");

            if (!run.Started)
            {
                return new EngineRunResultDto
                {
                    Started = false,
                    Message = "מנוע השידוכים כבר רץ כרגע. נסו שוב בעוד כמה דקות."
                };
            }

            return new EngineRunResultDto
            {
                Started = true,
                Message = run.Error == null
                    ? $"הריצה הסתיימה: {run.DonationsProcessed} תרומות נבדקו, נוצרו {run.RoutesCreated} מסלולים ({run.MatchedCount} תרומות שובצו)."
                    : "הריצה הסתיימה עם שגיאה. פרטים ביומן השרת.",
                DonationsProcessed = run.DonationsProcessed,
                MatchedCount = run.MatchedCount,
                RoutesCreated = run.RoutesCreated,
                DurationSeconds = Math.Round(run.DurationSeconds, 1),
                FailedConstraints = run.FailedConstraints.Take(50).ToList()
            };
        }

        // תוקן: engine-status החזיר ערכים קבועים. עכשיו - הריצה האחרונה בפועל + מספרים מהמסד.
        public async Task<EngineStatusDto> GetEngineStatusAsync()
        {
            var (running, last) = _runState.Snapshot();

            return new EngineStatusDto
            {
                Status = running ? "Running" : "Idle",
                LastRunAt = last?.FinishedAt,
                LastRunTime = last?.FinishedAt is DateTime finished
                    ? LocalClock.ToLocal(finished).ToString("dd/MM/yyyy HH:mm")
                    : "טרם בוצעה ריצה מאז עליית השרת",
                LastRunTrigger = last?.Trigger,
                MatchedCount = last?.MatchedCount ?? 0,
                RoutesCreated = last?.RoutesCreated ?? 0,
                DonationsProcessed = last?.DonationsProcessed ?? 0,
                LastRunDurationSeconds = last == null ? null : Math.Round(last.DurationSeconds, 1),
                FailedConstraints = last?.FailedConstraints.Take(50).ToList() ?? new List<string>(),
                PendingDonations = await _donationRepository.CountByStatusAsync(DonationStatus.Pending),
                AwaitingVolunteerResponse = await _matchRepository.CountByStatusSinceAsync(MatchStatus.AwaitingVolunteerConfirmation, DateTime.MinValue),
                ActiveAssignments = await _matchRepository.CountActiveAsync(),
                Schedule = _policy.Current.EngineCron
            };
        }

        // תוקן: שיבוץ ידני יצר את השידוך פעמיים (פעם בפונקציה שהוזרקה ב-Program.cs ופעם כאן),
        // הניסיון השני נחסם באינדקס הייחודי, המנהל קיבל 500 והמתנדב שני מיילים.
        // עכשיו: אותו מנוע, שידוך אחד, מייל אחד.
        public async Task<AssignDonationResultDto?> AssignDonationAsync(int donationId)
        {
            if (!await _donationRepository.ExistsAsync(donationId))
                return null;

            var outcome = await _engine.MatchDonationAsync(donationId, "Manual");
            if (outcome == null)
                throw new ConflictException("מנוע השידוכים רץ כרגע. נסו שוב בעוד כמה רגעים.");

            return new AssignDonationResultDto
            {
                Matched = outcome.Matched,
                MatchId = outcome.MatchId,
                RouteId = outcome.RouteId,
                Message = outcome.Message
            };
        }

        // =========================================================
        // תגובת המתנדב - ברמת המסלול
        // =========================================================

        public async Task<RouteDto> AcceptAsync(int id, CurrentUserDto currentUser)
        {
            var (match, volunteerId) = await LoadOwnMatchAsync(id, currentUser);
            await _workflow.AcceptAsync(match, volunteerId);
            return await BuildRouteDtoAsync(match);
        }

        public async Task DeclineAsync(int id, CurrentUserDto currentUser)
        {
            var (match, volunteerId) = await LoadOwnMatchAsync(id, currentUser);
            await _workflow.DeclineAsync(match, volunteerId, MatchStatus.Declined);
        }

        public async Task<RouteDto> StartRouteAsync(int routeId, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                throw new NotFoundException("המסלול לא נמצא.");

            await _workflow.StartRouteAsync(routeId, caller.Volunteer.Id);
            var route = await _routeRepository.GetWithDetailsAsync(routeId) ?? throw new NotFoundException("המסלול לא נמצא.");
            return ToRouteDto(route);
        }

        public async Task PickUpAsync(int id, CurrentUserDto currentUser)
        {
            var (match, volunteerId) = await LoadOwnMatchAsync(id, currentUser);
            await _workflow.PickUpAsync(match, volunteerId);
        }

        public async Task CompleteAsync(int id, CurrentUserDto currentUser)
        {
            var match = await _matchRepository.GetByIdWithDetailsAsync(id) ?? throw new NotFoundException(MatchNotFound);

            var caller = await _caller.GetAsync(currentUser);
            if (!caller.IsAdmin && !caller.OwnsMatchAsVolunteer(match))
                throw new NotFoundException(MatchNotFound);

            await _workflow.DeliverAsync(match);
        }

        public async Task<int> MarkNoShowAsync(int id, string? reason)
        {
            var match = await _matchRepository.GetByIdWithDetailsAsync(id) ?? throw new NotFoundException(MatchNotFound);
            return await _workflow.MarkNoShowAsync(match, reason, automatic: false);
        }

        // =========================================================
        // מסלולים
        // =========================================================

        public async Task<RouteDto?> GetRouteAsync(int routeId, CurrentUserDto currentUser)
        {
            var route = await _routeRepository.GetWithDetailsAsync(routeId);
            if (route == null)
                return null;

            var caller = await _caller.GetAsync(currentUser);
            bool allowed = caller.IsAdmin ||
                           (caller.Volunteer != null && route.VolunteerId == caller.Volunteer.Id) ||
                           route.Matches.Any(m => caller.IsMatchNgo(m) || caller.OwnsDonation(m.Donation));

            return allowed ? ToRouteDto(route) : null;
        }

        public async Task<RouteDto?> GetMyActiveRouteAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                return null;

            var route = await _routeRepository.GetActiveForVolunteerAsync(caller.Volunteer.Id);
            return route == null ? null : ToRouteDto(route);
        }

        // =========================================================
        // קישור מהמייל
        // =========================================================

        public async Task<RouteDto?> GetRouteByLinkAsync(string token)
        {
            var route = await LoadRouteForLinkAsync(token);
            return route == null ? null : ToRouteDto(route);
        }

        public async Task<RouteDto> RespondByLinkAsync(RespondByLinkDto dto)
        {
            var route = await LoadRouteForLinkAsync(dto.Token)
                        ?? throw new NotFoundException("הקישור אינו בתוקף (פג, כבר נוצל או שגוי).");

            var match = route.Matches.FirstOrDefault(m => m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                        ?? throw new ArgumentException("הבקשה כבר אינה ממתינה לתגובה.");

            // הטוקן נשלל (Revoke) בתוך האישור/הדחייה - כך שאי אפשר להשתמש בו שוב
            if (dto.Accept)
                await _workflow.AcceptAsync(match, route.VolunteerId);
            else
                await _workflow.DeclineAsync(match, route.VolunteerId, MatchStatus.Declined);

            var fresh = await _routeRepository.GetWithDetailsAsync(route.Id) ?? route;
            return ToRouteDto(fresh);
        }

        // =========================================================
        // עזר
        // =========================================================

        private async Task<VolunteerRoute?> LoadRouteForLinkAsync(string token)
        {
            var actionToken = await _actionTokens.FindValidLinkTokenAsync(ActionTokenPurpose.MatchResponse, token);
            if (actionToken?.TargetId == null)
                return null;

            var route = await _routeRepository.GetWithDetailsAsync(actionToken.TargetId.Value);

            // הטוקן שייך למתנדב של המסלול בלבד
            if (route?.Volunteer == null || route.Volunteer.UserId != actionToken.UserId)
                return null;

            return route;
        }

        // שידוך של המתנדב המחובר בלבד. שידוך שלא קיים ושידוך של מתנדב אחר מקבלים אותה תשובה (404).
        private async Task<(Match Match, int VolunteerId)> LoadOwnMatchAsync(int id, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                throw new NotFoundException(MatchNotFound);

            // מחפשים שידוך עם המזהה הזה *וגם* של המתנדב המחובר
            var match = await _matchRepository.GetOwnedByVolunteerAsync(id, caller.Volunteer.Id)
                        ?? throw new NotFoundException(MatchNotFound);

            return (match, caller.Volunteer.Id);
        }

        private async Task<RouteDto> BuildRouteDtoAsync(Match match)
        {
            if (match.VolunteerRouteId.HasValue)
            {
                var route = await _routeRepository.GetWithDetailsAsync(match.VolunteerRouteId.Value);
                if (route != null)
                    return ToRouteDto(route);
            }

            return new RouteDto
            {
                Id = 0,
                Status = match.Status.ToString(),
                VolunteerId = match.VolunteerId,
                NgoId = match.NgoId,
                Stops = new List<RouteStopDto> { ToStop(match, 1) }
            };
        }

        // האם המשתמש המחובר קשור לשידוך: מנהל, המתנדב, העמותה, או הספק של התרומה.
        private static bool IsParticipant(CallerProfile caller, Match match) =>
            caller.IsAdmin || caller.OwnsMatchAsVolunteer(match) || caller.IsMatchNgo(match) || caller.OwnsDonation(match.Donation);

        public static RouteDto ToRouteDto(VolunteerRoute route)
        {
            var matches = (route.Matches ?? new List<Match>())
                .OrderBy(m => m.EstimatedArrival ?? DateTime.MaxValue)
                .ToList();

            // בתצוגה - השידוכים הרלוונטיים (פעילים / שהושלמו). ניסיונות שנדחו לא מוצגים כתחנות.
            var shown = matches.Where(m => m.IsActive || m.Status == MatchStatus.Completed).ToList();
            if (shown.Count == 0) shown = matches;

            var ngo = shown.Select(m => m.Ngo).FirstOrDefault(n => n != null);
            var awaiting = shown.Where(m => m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation).ToList();

            return new RouteDto
            {
                Id = route.Id,
                Status = route.Status.ToString(),
                VolunteerId = route.VolunteerId,
                VolunteerName = route.Volunteer?.User?.Name,
                ScheduledAt = route.ScheduledAt,
                ResponseDeadline = awaiting.Select(m => m.ResponseDeadline).Where(d => d.HasValue).Min(),
                AwaitingResponse = awaiting.Count > 0,
                TotalWeightKg = route.TotalWeightKg,
                EstimatedTotalDurationMinutes = route.EstimatedTotalDurationMinutes,
                EstimatedTotalDistanceKm = route.EstimatedTotalDistanceKm,
                MatchScore = route.MatchScore,
                Polyline = route.GoogleDirectionsPolyline,
                NgoId = ngo?.Id ?? 0,
                NgoName = ngo?.NgoName,
                NgoAddress = ngo?.Address,
                NgoLatitude = ngo?.Latitude ?? 0,
                NgoLongitude = ngo?.Longitude ?? 0,
                EstimatedNgoArrival = route.ScheduledAt.AddMinutes(route.EstimatedTotalDurationMinutes ?? 0),
                VolunteerLatitude = route.Volunteer?.LocationUpdatedAt != null ? route.Volunteer.CurrentLatitude : null,
                VolunteerLongitude = route.Volunteer?.LocationUpdatedAt != null ? route.Volunteer.CurrentLongitude : null,
                Stops = shown.Select((m, i) => ToStop(m, i + 1)).ToList()
            };
        }

        private static RouteStopDto ToStop(Match m, int order) => new RouteStopDto
        {
            Order = order,
            MatchId = m.Id,
            DonationId = m.DonationId,
            Status = m.Status.ToString(),
            SupplierName = m.Donation?.Supplier?.BusinessName,
            Address = m.Donation?.Supplier?.Address,
            Latitude = m.Donation?.Latitude ?? 0,
            Longitude = m.Donation?.Longitude ?? 0,
            EstimatedArrival = m.EstimatedArrival,
            LatestPickupTime = m.Donation?.LatestPickupTime ?? TimeSpan.Zero,
            WeightKg = m.Donation?.TotalWeightKg() ?? 0,
            Items = (m.Donation?.DonationFoodCategories ?? new List<DonationFoodCategory>())
                .Select(c => new DonationCategoryLineDto
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.Category?.CategoryName,
                    Quantity = c.Quantity,
                    WeightKg = c.WeightKg
                }).ToList()
        };
    }
}
