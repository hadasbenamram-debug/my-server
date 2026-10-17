using BLL.Algoritems;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Services;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// מנוע השידוכים - המסלול היחיד שיוצר שידוכים במערכת.
    ///
    /// תוקן: היו שלושה מסלולי שידוך עם כללים שונים (ה-batch, DonationAssignmentService+Backtracking,
    /// ו-RunMatchingCycleAsync). עכשיו כולם עוברים כאן, עם אותם אילוצים קשים:
    ///   * הריצה השעתית (Hangfire) וההפעלה הידנית (api/matches/run-engine) -> RunBatchAsync
    ///   * שיבוץ ידני של תרומה (api/matches/assign-donation) -> MatchDonationAsync
    ///   * שיבוץ מחדש אחרי דחייה / אי-מענה / No-Show -> ReassignDonationAsync (ג'וב מיידי)
    ///
    /// לכל תרומה (לפי סדר דחיפות מתוך Max-Heap): בחירת עמותה מאושרת שיכולה לקבל אותה בזמן, סינון
    /// מתנדבים (מאושרים, זמינים, רישיון בתוקף, זמינות ואזורים), דירוג, בניית מסלולים (Knapsack + VRP)
    /// לכמה מתנדבים מובילים (MaxRoutesEvaluatedPerDonation), ושמירת המסלול עם ה-Matching Score הגבוה.
    /// </summary>
    public class DonationMatchingBatchService
    {
        public const string LockResource = "matching-engine";

        private readonly IDonationRepository _donationRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IRegionRepository _regionRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly INgoSelectionService _ngoSelectionService;
        private readonly DonationMatchingOrchestrator _orchestrator;
        private readonly MatchCreationService _matchCreationService;
        private readonly RegionAvailabilityMatchingService _regionMatcher;
        private readonly VolunteerRankingService _rankingService;
        private readonly BacktrackingAssignmentService _backtracking;
        private readonly IGoogleMapsDistanceService _distances;
        private readonly AdminEscalationService _escalation;
        private readonly IMatchingPolicyProvider _policyProvider;
        private readonly IEngineLock _engineLock;
        private readonly EngineRunState _runState;
        private readonly ILogger<DonationMatchingBatchService> _logger;
        private readonly TimeProvider _clock;

        public DonationMatchingBatchService(
            IDonationRepository donationRepository,
            INgoRepository ngoRepository,
            IVolunteerRepository volunteerRepository,
            IRegionRepository regionRepository,
            IMatchRepository matchRepository,
            INgoSelectionService ngoSelectionService,
            DonationMatchingOrchestrator orchestrator,
            MatchCreationService matchCreationService,
            RegionAvailabilityMatchingService regionMatcher,
            VolunteerRankingService rankingService,
            BacktrackingAssignmentService backtracking,
            IGoogleMapsDistanceService distances,
            AdminEscalationService escalation,
            IMatchingPolicyProvider policyProvider,
            IEngineLock engineLock,
            EngineRunState runState,
            ILogger<DonationMatchingBatchService> logger,
            TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
            _donationRepository = donationRepository;
            _ngoRepository = ngoRepository;
            _volunteerRepository = volunteerRepository;
            _regionRepository = regionRepository;
            _matchRepository = matchRepository;
            _ngoSelectionService = ngoSelectionService;
            _orchestrator = orchestrator;
            _matchCreationService = matchCreationService;
            _regionMatcher = regionMatcher;
            _rankingService = rankingService;
            _backtracking = backtracking;
            _distances = distances;
            _escalation = escalation;
            _policyProvider = policyProvider;
            _engineLock = engineLock;
            _runState = runState;
            _logger = logger;
        }

        // ההקשר של ריצה אחת: נטען פעם אחת ומשותף לכל התרומות בריצה
        private sealed class RunContext
        {
            public DateTime Now { get; init; }
            public MatchingPolicyOptions Policy { get; init; } = null!;
            public List<Donation> Donations { get; init; } = new();
            public List<Ngo> Ngos { get; init; } = new();
            public List<Volunteer> Volunteers { get; init; } = new();
            public HashSet<int> BusyVolunteerIds { get; init; } = new();
            public HashSet<int> AssignedDonationIds { get; } = new();
            public Dictionary<int, Region> Regions { get; init; } = new();
        }

        public sealed record DonationMatchOutcome(bool Matched, int? RouteId, int? MatchId, string Message, IReadOnlyList<int> DonationIds);

        // =========================================================
        // Hangfire entry points
        // =========================================================

        // הריצה השעתית. DisableConcurrentExecution: ריצה לא מתחילה אם הקודמת עוד רצה.
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        [AutomaticRetry(Attempts = 0)]
        public Task RunScheduledAsync() => RunBatchAsync("Scheduled");

        // שיבוץ מחדש מיידי של תרומה (אחרי דחייה / אי-מענה / No-Show). אם המנוע תפוס - Hangfire ינסה שוב.
        [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 30, 60, 120, 300, 600 })]
        public async Task ReassignDonationAsync(int donationId)
        {
            var outcome = await MatchDonationAsync(donationId, "Reassign");
            if (outcome == null)
                throw new InvalidOperationException("Matching engine is busy; the re-assignment will be retried.");
        }

        // =========================================================
        // ריצה מלאה
        // =========================================================

        public async Task<EngineRunResult> RunBatchAsync(string trigger = "Scheduled")
        {
            using var engineLock = _engineLock.TryAcquire(LockResource, TimeSpan.FromSeconds(5));
            if (engineLock == null)
            {
                _logger.LogInformation("Matching engine: another run is in progress; {Trigger} run skipped.", trigger);
                return EngineRunResult.NotStarted(trigger);
            }

            var result = new EngineRunResult { Trigger = trigger, StartedAt = DateTime.UtcNow };
            _runState.Started(result);

            try
            {
                var ctx = await BuildContextAsync();
                var heap = new PriorityHeapService<Donation, int>(d => d.Id, (a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

                foreach (var d in ctx.Donations)
                    heap.Insert(d, PriorityCalculator.Calculate(d, ctx.Now, ctx.Policy.UrgencyTimeConstantMinutes));

                _logger.LogInformation("Matching engine ({Trigger}): {Count} open donations.", trigger, heap.Count);

                while (heap.Count > 0)
                {
                    var donation = heap.ExtractMax();
                    if (donation == null) break;
                    if (ctx.AssignedDonationIds.Contains(donation.Id)) continue;   // נאספה כבר במסלול של תרומה אחרת

                    result.DonationsProcessed++;

                    try
                    {
                        var outcome = await TryMatchAsync(donation, ctx, result.FailedConstraints);
                        if (outcome.Matched)
                        {
                            result.RoutesCreated++;
                            result.MatchedCount += outcome.DonationIds.Count;
                            foreach (var id in outcome.DonationIds)
                            {
                                var routed = ctx.Donations.FirstOrDefault(x => x.Id == id);
                                if (routed != null) heap.Remove(routed);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Matching engine: failed processing donation {DonationId}.", donation.Id);
                        result.FailedConstraints.Add($"תרומה #{donation.Id}: שגיאה פנימית בעיבוד.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Matching engine: fatal error.");
                result.Error = ex.Message;
            }
            finally
            {
                result.FinishedAt = DateTime.UtcNow;
                _runState.Finished(result);
            }

            _logger.LogInformation("Matching engine ({Trigger}) finished: {Processed} donations, {Routes} routes, {Matched} matched, {Seconds:0.0}s.",
                trigger, result.DonationsProcessed, result.RoutesCreated, result.MatchedCount, result.DurationSeconds);

            return result;
        }

        // =========================================================
        // תרומה אחת (שיבוץ ידני / שיבוץ מחדש) - אותם כללים בדיוק
        // =========================================================

        // null = המנוע תפוס כרגע בריצה אחרת
        public async Task<DonationMatchOutcome?> MatchDonationAsync(int donationId, string trigger = "Manual")
        {
            using var engineLock = _engineLock.TryAcquire(LockResource, TimeSpan.FromSeconds(30));
            if (engineLock == null)
                return null;

            var ctx = await BuildContextAsync();
            var donation = ctx.Donations.FirstOrDefault(d => d.Id == donationId);

            if (donation == null)
            {
                var existing = await _donationRepository.GetByIdWithCategoriesAsync(donationId);
                string message = existing == null
                    ? "התרומה לא נמצאה."
                    : existing.Status != DonationStatus.Pending
                        ? "התרומה אינה ממתינה לשיבוץ."
                        : "התרומה אינה זמינה לשיבוץ היום (פג תוקף / נוצרה ביום אחר / הספק לא מאושר).";
                return new DonationMatchOutcome(false, null, null, message, Array.Empty<int>());
            }

            var failures = new List<string>();
            var outcome = await TryMatchAsync(donation, ctx, failures);
            if (!outcome.Matched && failures.Count > 0)
                return outcome with { Message = failures[0] };

            _logger.LogInformation("Matching engine ({Trigger}) for donation {DonationId}: {Message}", trigger, donationId, outcome.Message);
            return outcome;
        }

        // =========================================================
        // ליבת השידוך
        // =========================================================

        private async Task<RunContext> BuildContextAsync()
        {
            // ההגדרות שהמנהל שמר ב-api/settings נכנסות לתוקף בכל ריצה
            await _policyProvider.ReloadAsync();

            var now = _clock.GetUtcNow().UtcDateTime;
            var localToday = LocalClock.ToLocal(now).Date;
            var dayStartUtc = LocalClock.ToUtc(localToday);

            var regions = (await _regionRepository.GetAllAsync()).ToDictionary(r => r.Id);

            return new RunContext
            {
                Now = now,
                Policy = _policyProvider.Current,
                // "כל תרומה מיועדת לאותו יום בלבד" - לפי היום המקומי, לא לפי תאריך ה-UTC
                Donations = await _donationRepository.GetAvailableDonationsAsync(now, dayStartUtc),
                // תוקן: עמותה שעדיין לא אושרה יכלה לקבל שידוך
                Ngos = await _ngoRepository.GetApprovedAsync(),
                // תוקן: מתנדב שעדיין לא אושר יכול היה לקבל שידוך
                Volunteers = await _volunteerRepository.GetAvailableVolunteersAsync(now),
                // מתנדב שכבר יש לו הצעה או מסלול פעיל לא מקבל הצעה נוספת
                BusyVolunteerIds = await _matchRepository.GetVolunteerIdsWithActiveMatchesAsync(),
                Regions = regions
            };
        }

        private async Task<DonationMatchOutcome> TryMatchAsync(Donation donation, RunContext ctx, List<string> failures)
        {
            var policy = ctx.Policy;

            if (donation.Status != DonationStatus.Pending)
                return NotMatched(donation, "התרומה אינה ממתינה לשיבוץ.");

            if (NgoAvailabilityHelper.GetPickupDateTime(donation) <= ctx.Now)
                return Fail(donation, failures, "שעת האיסוף האחרונה כבר עברה.", escalate: false);

            // --- עמותה: מאושרת, פתוחה בזמן ההגעה האפשרי, לפני התפוגה ---
            var ngoSelection = await _ngoSelectionService.SelectBestNgoAsync(donation, ctx.Ngos, ctx.Now);
            if (ngoSelection == null)
                return await EscalateAsync(donation, failures, "אין עמותה מאושרת שיכולה לקבל את התרומה בזמן.");

            var ngo = ngoSelection.Value.Ngo;

            // --- מתנדבים: Hard Constraints ---
            var alreadyFailed = await _matchRepository.GetVolunteerIdsWhoFailedDonationAsync(donation.Id);
            var pool = ctx.Volunteers
                .Where(v => !ctx.BusyVolunteerIds.Contains(v.Id) && !alreadyFailed.Contains(v.Id))
                .Where(v => v.MaxCapacityKg >= donation.TotalWeightKg())
                .ToList();

            var candidates = _regionMatcher.FindCandidates(donation, ngo, pool, ctx.Now);
            if (candidates.Count == 0)
                return await EscalateAsync(donation, failures, "אין מתנדב זמין עם זמינות מתאימה (יום, שעה, אזור וכיוון נסיעה).");

            // --- מרחק נסיעה (Google, עם מטמון) מנקודת היציאה של כל מתנדב ---
            var distanceById = new Dictionary<int, double>();
            var startById = new Dictionary<int, GeoCoordinate>();

            foreach (var v in candidates)
            {
                var start = ResolveStartLocation(v, ctx);
                if (start == null) continue;

                var (km, _) = await _distances.GetDrivingDistanceAsync(start.Latitude, start.Longitude, donation.Latitude, donation.Longitude);
                if (km > policy.MaxSearchRadiusKm) continue;

                distanceById[v.Id] = km;
                startById[v.Id] = start;
            }

            var inRange = candidates.Where(v => distanceById.ContainsKey(v.Id)).ToList();
            if (inRange.Count == 0)
                return await EscalateAsync(donation, failures, "כל המתנדבים הזמינים נמצאים מחוץ לטווח החיפוש.");

            // --- דירוג המתנדבים (מרחק, דחיפות, קיבולת, אמינות) ---
            var ranked = _rankingService.RankVolunteers(inRange, donation, distanceById, policy.MaxSearchRadiusKm, asOfUtc: ctx.Now);
            var rankedCandidates = ranked
                .Select(v => new VolunteerCandidate(
                    v,
                    distanceById[v.Id],
                    _rankingService.Score(v, donation, distanceById[v.Id], policy.MaxSearchRadiusKm, ctx.Now),
                    startById[v.Id]))
                .ToList();

            // --- בניית מסלולים לכמה מועמדים מובילים (Backtracking) ---
            var routes = await _backtracking.FindRoutesAsync(
                rankedCandidates,
                policy.MaxSearchRadiusKm,
                policy.MaxRoutesEvaluatedPerDonation,
                c => _orchestrator.BuildRouteAsync(
                    c.Volunteer,
                    ctx.Donations.Where(d => !ctx.AssignedDonationIds.Contains(d.Id)).ToList(),
                    c.StartLocation,
                    ngo,
                    ctx.Now,
                    anchorDonationId: donation.Id));

            if (routes.Count == 0)
                return await EscalateAsync(donation, failures,
                    "לא נמצא מסלול שעומד בכל האילוצים (זמני איסוף, שעות קבלה בעמותה, זמינות המתנדב וקיבולת הרכב).");

            // --- Matching Score למסלול ובחירת המסלול הטוב ביותר ---
            ScoreRoutes(routes);

            foreach (var best in routes.OrderByDescending(r => r.Score))
            {
                var created = await _matchCreationService.CreateRouteAsync(best.Candidate.Volunteer, ngo, best.Plan, best.Score);
                if (created == null)
                    continue;   // אחת התרומות שובצה במקביל - מנסים את המסלול הבא

                ctx.BusyVolunteerIds.Add(best.Candidate.Volunteer.Id);
                var donationIds = best.Plan.Stops.Select(s => s.DonationId).ToList();
                foreach (var id in donationIds)
                    ctx.AssignedDonationIds.Add(id);

                var anchorMatch = created.Matches?.FirstOrDefault(m => m.DonationId == donation.Id);
                _logger.LogInformation("Matching engine: route {RouteId} ({Stops} stops, score {Score:0.0}) offered to volunteer {VolunteerId}.",
                    created.Id, donationIds.Count, best.Score, best.Candidate.Volunteer.Id);

                return new DonationMatchOutcome(true, created.Id, anchorMatch?.Id,
                    $"נוצר מסלול עם {donationIds.Count} תחנות ונשלחה בקשה למתנדב.", donationIds);
            }

            return Fail(donation, failures, "התרומה שובצה במקביל בתהליך אחר.", escalate: false);
        }

        // ציון למסלול: 60% ציון המתנדב (מרחק, דחיפות, קיבולת, אמינות) + 40% יעילות המסלול
        // (ערך ה-Knapsack ביחס לזמן הנסיעה, מנורמל מול המסלול היעיל ביותר שנבדק). סקאלה 0-100.
        public static void ScoreRoutes(IReadOnlyList<RouteCandidate> routes)
        {
            if (routes.Count == 0) return;

            double Efficiency(RouteCandidate r) => r.Plan.KnapsackValue / (1.0 + Math.Max(0, r.Plan.TotalDurationMinutes) / 60.0);
            double maxEfficiency = routes.Max(Efficiency);

            foreach (var r in routes)
            {
                double normalizedEfficiency = maxEfficiency > 0 ? Efficiency(r) / maxEfficiency : 0;
                r.Score = Math.Round(100.0 * (0.6 * Math.Clamp(r.Candidate.Score, 0, 1) + 0.4 * normalizedEfficiency), 2);
            }
        }

        // נקודת היציאה: מיקום GPS עדכני, ואם אין - מרכז אזור המוצא של משבצת הזמינות של היום
        private static GeoCoordinate? ResolveStartLocation(Volunteer v, RunContext ctx)
        {
            bool hasFreshGps = (v.CurrentLatitude != 0 || v.CurrentLongitude != 0) &&
                               v.LocationUpdatedAt.HasValue &&
                               (ctx.Now - v.LocationUpdatedAt.Value).TotalMinutes <= ctx.Policy.MaxLocationAgeMinutes;

            if (hasFreshGps)
                return new GeoCoordinate { Latitude = v.CurrentLatitude, Longitude = v.CurrentLongitude };

            var local = LocalClock.ToLocal(ctx.Now);
            var slot = v.AvailabilitySlots?
                .Where(s => s.Day == local.DayOfWeek && AvailabilityHelper.IsWithinTimeWindow(s, local.TimeOfDay))
                .FirstOrDefault();

            if (slot != null && ctx.Regions.TryGetValue(slot.RegionId, out var region))
                return new GeoCoordinate { Latitude = region.CenterLatitude, Longitude = region.CenterLongitude };

            return null;
        }

        private async Task<DonationMatchOutcome> EscalateAsync(Donation donation, List<string> failures, string reason)
        {
            try
            {
                await _escalation.NotifyAdminsAsync(donation, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Escalation for donation {DonationId} failed.", donation.Id);
            }

            return Fail(donation, failures, reason, escalate: false);
        }

        private static DonationMatchOutcome Fail(Donation donation, List<string> failures, string reason, bool escalate)
        {
            failures.Add($"תרומה #{donation.Id}: {reason}");
            return NotMatched(donation, reason);
        }

        private static DonationMatchOutcome NotMatched(Donation donation, string reason) =>
            new(false, null, null, reason, Array.Empty<int>());
    }
}
