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
        private readonly TimeProvider _clock;   // שעון אחד לכל המערכת (בבדיקות - שעון קבוע)
        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

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
            ILogger<MatchService> logger,
            TimeProvider? clock = null)
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
            _clock = clock ?? TimeProvider.System;
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

        // תיקון 16: בעבר עמוד ההיסטוריה של המתנדב השתמש ב-matches/active - שמחזיר רק שיבוצים פעילים,
        // ולכן שיבוץ שהסתיים (נמסר) נעלם מההיסטוריה במקום להופיע בה.
        private const int HistoryLimit = 200;

        public async Task<List<MatchResponseDto>> GetMyVolunteerHistoryAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                return new List<MatchResponseDto>();

            var matches = await _matchRepository.GetHistoryForVolunteerAsync(caller.Volunteer.Id, HistoryLimit);
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

        public async Task<RouteResponseOutcome> AcceptAsync(int id, CurrentUserDto currentUser)
        {
            var (match, volunteerId) = await LoadOwnMatchAsync(id, currentUser);
            var result = await _workflow.AcceptAsync(match, volunteerId);
            return new RouteResponseOutcome(result, result == RouteResponseResult.Done ? await BuildRouteDtoAsync(match) : null);
        }

        public async Task<RouteResponseResult> DeclineAsync(int id, CurrentUserDto currentUser)
        {
            var (match, volunteerId) = await LoadOwnMatchAsync(id, currentUser);
            return await _workflow.DeclineAsync(match, volunteerId, MatchStatus.Declined);
        }

        public async Task<RouteDto> StartRouteAsync(int routeId, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                throw new NotFoundException("המסלול לא נמצא.");

            await _workflow.StartRouteAsync(routeId, caller.Volunteer.Id);
            var route = await _routeRepository.GetWithDetailsAsync(routeId) ?? throw new NotFoundException("המסלול לא נמצא.");
            return ToRouteDto(route, Now);
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

        public async Task<int> MarkNoShowAsync(int id, string? reason, bool autoReassign = true)
        {
            var match = await _matchRepository.GetByIdWithDetailsAsync(id) ?? throw new NotFoundException(MatchNotFound);
            return await _workflow.MarkNoShowAsync(match, reason, automatic: false, autoReassign: autoReassign);
        }

        // =========================================================
        // תיקון 36: טיפול ידני של המנהל באי-הגעה
        // =========================================================

        private const int NoShowHistoryLimit = 50;

        private static readonly MatchStatus[] AwaitingPickupStatuses = { MatchStatus.Confirmed, MatchStatus.InPickup };

        public async Task<NoShowBoardDto> GetNoShowBoardAsync()
        {
            var now = Now;
            var localToday = LocalClock.ToLocal(now).Date;

            // מסלולים שאושרו ויש בהם תחנה שעוד לא נאספה (Confirmed / InPickup) - מועמדים ל-No-Show
            var active = await _matchRepository.GetActiveMatchesAsync();
            var awaiting = active
                .Where(m => m.PickedUpAt == null && AwaitingPickupStatuses.Contains(m.Status))
                .GroupBy(m => m.VolunteerRouteId ?? -m.Id)
                .Select(g =>
                {
                    var stops = g.OrderBy(m => m.EstimatedArrival ?? DateTime.MaxValue).ThenBy(m => m.Id).ToList();
                    var first = stops[0];
                    var eta = stops.Select(m => m.EstimatedArrival).FirstOrDefault(e => e.HasValue);
                    return new AwaitingPickupRouteDto
                    {
                        RouteId = first.VolunteerRouteId,
                        MatchId = first.Id,
                        VolunteerId = first.VolunteerId,
                        VolunteerName = first.Volunteer?.User?.Name ?? $"מתנדב #{first.VolunteerId}",
                        VolunteerPhone = first.Volunteer?.User?.Phone,
                        Started = stops.Any(m => m.Status == MatchStatus.InPickup),
                        NextPickupEta = eta,
                        MinutesLate = eta.HasValue && now > eta.Value ? (int)Math.Floor((now - eta.Value).TotalMinutes) : 0,
                        LastLocationAt = first.Volunteer?.LocationUpdatedAt,
                        Stops = stops.Select(m => new AwaitingPickupStopDto
                        {
                            MatchId = m.Id,
                            DonationId = m.DonationId,
                            SupplierName = m.Donation?.Supplier?.BusinessName,
                            PickupAddress = m.Donation?.Supplier?.Address,
                            EstimatedArrival = m.EstimatedArrival,
                            Status = m.Status.ToString()
                        }).ToList()
                    };
                })
                .OrderByDescending(r => r.MinutesLate)
                .ThenBy(r => r.NextPickupEta ?? DateTime.MaxValue)
                .ToList();

            // אירועי No-Show אחרונים, ומה קרה לתרומה מאז
            var noShows = await _matchRepository.GetPageAsync(0, NoShowHistoryLimit, MatchStatus.NoShow);
            var events = new List<NoShowEventDto>(noShows.Items.Count);
            var currentByDonation = new Dictionary<int, DAL.Classes.Match?>();
            foreach (var m in noShows.Items)
            {
                if (!currentByDonation.TryGetValue(m.DonationId, out var current))
                {
                    current = await _matchRepository.GetCurrentForDonationAsync(m.DonationId);
                    currentByDonation[m.DonationId] = current;
                }

                // המצב העדכני של התרומה מהשידוך הנוכחי שלה (ה-Include של אירוע ישן יכול להיות מאותה טעינה)
                var donation = current?.Donation ?? m.Donation;
                bool reassigned = current != null && current.Id != m.Id && current.IsActive;
                bool canAssign = donation != null &&
                                 donation.Status == DonationStatus.Pending &&
                                 NgoAvailabilityHelper.GetPickupDateTime(donation) > now &&
                                 LocalClock.ToLocal(donation.CreatedAt).Date == localToday;

                events.Add(new NoShowEventDto
                {
                    MatchId = m.Id,
                    DonationId = m.DonationId,
                    RouteId = m.VolunteerRouteId,
                    VolunteerName = m.Volunteer?.User?.Name ?? $"מתנדב #{m.VolunteerId}",
                    VolunteerPhone = m.Volunteer?.User?.Phone,
                    SupplierName = m.Donation?.Supplier?.BusinessName,
                    PickupAddress = m.Donation?.Supplier?.Address,
                    AssignedAt = m.AssignedAt,
                    EstimatedArrival = m.EstimatedArrival,
                    DonationStatus = donation?.Status.ToString() ?? string.Empty,
                    ReassignedToVolunteerName = reassigned ? current!.Volunteer?.User?.Name ?? $"מתנדב #{current.VolunteerId}" : null,
                    CanAssign = canAssign
                });
            }

            return new NoShowBoardDto { GeneratedAt = now, AwaitingPickup = awaiting, NoShows = events };
        }

        public async Task<ManualAssignmentOptionsDto?> GetManualAssignmentOptionsAsync(int donationId)
        {
            var result = await _engine.GetManualCandidatesAsync(donationId);
            if (result.Donation == null)
                return null;

            // התרומות שהמנוע טוען לא כוללות את הספק - לשם ולכתובת שבמסך טוענים אותה עם הספק
            var donation = result.Donation.Supplier != null
                ? result.Donation
                : await _donationRepository.GetByIdWithCategoriesAsync(result.Donation.Id) ?? result.Donation;

            return new ManualAssignmentOptionsDto
            {
                DonationId = donation.Id,
                SupplierName = donation.Supplier?.BusinessName,
                PickupAddress = donation.Supplier?.Address,
                WeightKg = Math.Round(donation.TotalWeightKg(), 1),
                PickupDeadline = result.PickupDeadline,
                Problem = result.Problem,
                Volunteers = result.Volunteers.Select(o => new ManualVolunteerOptionDto
                {
                    VolunteerId = o.Volunteer.Id,
                    Name = o.Volunteer.User?.Name ?? $"מתנדב #{o.Volunteer.Id}",
                    Phone = o.Volunteer.User?.Phone,
                    MaxCapacityKg = o.Volunteer.MaxCapacityKg,
                    ReliabilityScore = Math.Round(o.Volunteer.ReliabilityScore, 1),
                    DistanceKm = o.DistanceKm,
                    HasFreshLocation = o.HasFreshLocation,
                    AvailableBySchedule = o.AvailableBySchedule,
                    FailedThisDonationBefore = o.FailedThisDonationBefore,
                    CanAssign = o.CanAssign,
                    Note = o.Note
                }).ToList()
            };
        }

        public async Task<AssignDonationResultDto> AssignDonationToVolunteerAsync(int donationId, int volunteerId, bool ignoreSchedule)
        {
            var outcome = await _engine.AssignToVolunteerAsync(donationId, volunteerId, ignoreSchedule)
                          ?? throw new ConflictException("מנוע השידוכים רץ כרגע. נסו שוב בעוד כמה רגעים.");

            return new AssignDonationResultDto
            {
                Matched = outcome.Matched,
                MatchId = outcome.MatchId,
                RouteId = outcome.RouteId,
                Message = outcome.Message
            };
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

            return allowed ? ToRouteDto(route, Now) : null;
        }

        public async Task<RouteDto?> GetMyActiveRouteAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Volunteer == null)
                return null;

            var route = await _routeRepository.GetActiveForVolunteerAsync(caller.Volunteer.Id);
            return route == null ? null : ToRouteDto(route, Now);
        }

        // המפה החיה של המנהל: כל המסלולים שמתנדבים נמצאים בהם עכשיו בדרך
        public async Task<List<RouteDto>> GetLiveRoutesAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (!caller.IsAdmin)
                throw new ForbiddenException("רק מנהל יכול לצפות בכל המסלולים.");

            var routes = await _routeRepository.GetInProgressWithDetailsAsync();
            return routes.Select(r => ToRouteDto(r, Now)).ToList();
        }

        // =========================================================
        // קישור מהמייל
        // =========================================================

        public async Task<RouteDto?> GetRouteByLinkAsync(string token)
        {
            var route = await LoadRouteForLinkAsync(token);
            return route == null ? null : ToRouteDto(route, Now);
        }

        // תיקון 12: קישור שפג / נוצל, או בקשה שכבר לא ממתינה - תוצאה ולא חריגה
        public async Task<RespondByLinkOutcome> RespondByLinkAsync(RespondByLinkDto dto)
        {
            var route = await LoadRouteForLinkAsync(dto.Token);
            if (route == null)
                return new RespondByLinkOutcome(RouteResponseResult.LinkInvalid, null, 0);

            var match = route.Matches.FirstOrDefault(m => m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation);

            // הטוקן נשלל (Revoke) בתוך האישור/הדחייה - כך שאי אפשר להשתמש בו שוב
            var result = match == null
                ? RouteResponseResult.NotAwaiting
                : dto.Accept
                    ? await _workflow.AcceptAsync(match, route.VolunteerId)
                    : await _workflow.DeclineAsync(match, route.VolunteerId, MatchStatus.Declined);

            var fresh = await _routeRepository.GetWithDetailsAsync(route.Id) ?? route;
            return new RespondByLinkOutcome(result, ToRouteDto(fresh, Now), route.Volunteer.UserId);
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
                    return ToRouteDto(route, Now);
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

        public static RouteDto ToRouteDto(VolunteerRoute route, DateTime? nowUtc = null)
        {
            var matches = (route.Matches ?? new List<Match>())
                .OrderBy(m => m.EstimatedArrival ?? DateTime.MaxValue)
                .ToList();

            // בתצוגה - השידוכים הרלוונטיים (פעילים / שהושלמו). ניסיונות שנדחו לא מוצגים כתחנות.
            var shown = matches.Where(m => m.IsActive || m.Status == MatchStatus.Completed).ToList();
            if (shown.Count == 0) shown = matches;

            var ngo = shown.Select(m => m.Ngo).FirstOrDefault(n => n != null);
            var awaiting = shown.Where(m => m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation).ToList();
            var deadline = awaiting.Select(m => m.ResponseDeadline).Where(d => d.HasValue).Min();
            // תיקון 42: הזמן מגיע מהשעון של השירות (אותו שעון של המנוע). בלי ערך - השעה הנוכחית.
            var now = nowUtc ?? DateTime.UtcNow;

            // המיקום של המתנדב נחשף לעמותה ולספקים רק בזמן שהוא בדרך (InProgress) - לא לפני היציאה ולא אחרי הסיום
            bool shareLocation = route.Status == RouteStatus.InProgress && route.Volunteer?.LocationUpdatedAt != null;

            return new RouteDto
            {
                Id = route.Id,
                Status = route.Status.ToString(),
                VolunteerId = route.VolunteerId,
                VolunteerName = route.Volunteer?.User?.Name,
                ScheduledAt = route.ScheduledAt,
                ResponseDeadline = deadline,
                ResponseSecondsLeft = awaiting.Count > 0 && deadline.HasValue
                    ? (int)Math.Max(0, Math.Floor((deadline.Value - now).TotalSeconds))
                    : null,
                // תיקון 13: אחרי שהזמן עבר כבר לא ממתינים לתגובה - גם אם הג'וב שמסמן "לא ענה בזמן" עוד לא רץ
                // (כך המתנדב לא רואה כפתור "אישור" אחרי 5 הדקות)
                AwaitingResponse = awaiting.Count > 0 && (deadline == null || deadline.Value > now),
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
                VolunteerLatitude = shareLocation ? route.Volunteer!.CurrentLatitude : null,
                VolunteerLongitude = shareLocation ? route.Volunteer!.CurrentLongitude : null,
                StartedAt = route.StartedAt,
                CompletedAt = route.CompletedAt,
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
            EarliestPickupTime = m.Donation?.EarliestPickupTime,   // תיקון 43
            WeightKg = m.Donation?.TotalWeightKg() ?? 0,
            PickedUpAt = m.PickedUpAt,
            DeliveredAt = m.DeliveredAt,
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
