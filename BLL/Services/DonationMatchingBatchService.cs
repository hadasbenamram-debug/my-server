using BLL.Algorithms;
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
    /// ריצה מלאה (תיקונים 19-30):
    ///   1. Max-Heap: סדר הדחיפות של התרומות (Heapify ב-O(n), כל שליפה O(log n)).
    ///   2. לכל תרומה: העמותות שיכולות לקבל אותה (שעות קבלה, קיבולת יומית בק"ג) - כמה חלופות, לא אחת.
    ///   3. מתנדבים: מאושרים, רישיון בתוקף, קיבולת, וחלון זמינות לפי מערכת השעות - עכשיו או עד הריצה הבאה.
    ///   4. KD-Tree: רק מתנדבים בטווח החיפוש (קו אווירי) נשאלים מול Google, ותרומות נוספות - רק ליד העוגן.
    ///   5. דירוג (Merge Sort) ו-Backtracking: מסלול לכל מתנדב מוביל, עם חלופה כשמסלול נכשל.
    ///   6. מסלול: Knapsack (מה לאסוף) + DP מדויק (מה נכנס בזמן, באיזה סדר, ומתי לצאת), לכמה עמותות.
    ///   7. Branch &amp; Bound גלובלי: איזה מסלולים יוצאים, כך שסך המזון המוצל (משוקלל בדחיפות ובאיכות) מקסימלי.
    ///   8. ניסיון נוסף לתרומות שנשארו (עם המתנדבים שנשארו פנויים), והסלמה למנהל רק כשבאמת אין סיכוי.
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
            // מתנדב + תרומה שהוא כבר דחה / לא ענה עליה בזמן / לא הגיע אליה - לא מציעים לו אותה שוב (תיקון 13)
            public HashSet<(int VolunteerId, int DonationId)> FailedPairs { get; init; } = new();
            public Dictionary<int, Region> Regions { get; init; } = new();

            // תיקון 21: הקיבולת שנשארה היום לכל עמותה, בק"ג (אין רשומה = ללא הגבלה). מתעדכן עם כל מסלול שנשמר.
            public Dictionary<int, double> NgoRemainingKg { get; init; } = new();

            // תיקון 30: אינדקס גיאוגרפי (KD-Tree) של התרומות הפתוחות - לאיתור תרומות נוספות ליד תרומת העוגן
            public GeoKdTree<Donation> DonationIndex { get; init; } = null!;

            // תיקון 27: ההצעה נשלחת עכשיו והמתנדב יכול לענות עד סוף חלון התגובה - זו היציאה המוקדמת ביותר
            public DateTime EarliestDepartureUtc => Now.AddMinutes(Math.Max(0, Policy.ResponseTimeoutMinutes));

            // תיקון 24: מתכננים יציאה עד הריצה הבאה. מה שמאוחר יותר יתוכנן בריצה הבאה, עם מידע עדכני.
            public DateTime LatestDepartureUtc => Now.AddMinutes(Math.Max(Policy.ResponseTimeoutMinutes, Policy.PlanningHorizonMinutes));
        }

        // המסלולים המועמדים של תרומה אחת, ואם אין - למה, והאם להסלים למנהל
        private sealed class DonationCandidates
        {
            public Donation Donation { get; init; } = null!;
            public List<RouteCandidate> Routes { get; } = new();
            public string? Reason { get; set; }
            public bool Escalate { get; set; } = true;
            public bool HasLaterAvailability { get; set; }
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

                // תיקון 9: בשבת לא שולחים הצעות ולא משבצים (וגם לא מסלימים למנהל) - הריצה שאחרי צאת השבת תמשיך
                if (ShabbatClock.IsShabbat(ctx.Now, ctx.Policy))
                {
                    _logger.LogInformation("Matching engine ({Trigger}): Shabbat - no offers are sent until Shabbat ends.", trigger);
                    result.FailedConstraints.Add(ShabbatClock.EngineMessage);
                    return result;
                }

                // ---------- שלב 1: Max-Heap - סדר הדחיפות (Heapify ב-O(n), שליפה ב-O(log n)) ----------
                var heap = new PriorityHeapService<Donation, int>(d => d.Id, (a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
                heap.LoadOrUpdateAll(ctx.Donations.Select(d =>
                    (d, PriorityCalculator.Calculate(d, ctx.Now, ctx.Policy.UrgencyTimeConstantMinutes))));

                var byUrgency = new List<Donation>(heap.Count);
                while (heap.Count > 0)
                    byUrgency.Add(heap.ExtractMax()!);

                _logger.LogInformation("Matching engine ({Trigger}): {Count} open donations.", trigger, byUrgency.Count);
                result.DonationsProcessed = byUrgency.Count;

                // ---------- שלבים 2-6: המסלולים המועמדים של כל תרומה ----------
                var candidates = new List<DonationCandidates>(byUrgency.Count);
                foreach (var donation in byUrgency)
                {
                    try
                    {
                        candidates.Add(await BuildCandidatesAsync(donation, ctx));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Matching engine: failed processing donation {DonationId}.", donation.Id);
                        result.FailedConstraints.Add($"תרומה #{donation.Id}: שגיאה פנימית בעיבוד.");
                    }
                }

                // ---------- שלב 7: Branch & Bound גלובלי - איזה מסלולים יוצאים ----------
                var selection = _backtracking.SelectRoutes(
                    candidates.Select(c => c.Donation.Id).ToList(),
                    candidates.ToDictionary(c => c.Donation.Id, c => (IReadOnlyList<RouteCandidate>)c.Routes),
                    ctx.NgoRemainingKg,
                    Math.Max(1, ctx.Policy.AssignmentSearchNodeLimit),
                    TimeSpan.FromSeconds(Math.Clamp(ctx.Policy.AssignmentSearchTimeLimitSeconds, 0.1, 600)));

                _logger.LogInformation(
                    "Matching engine: assignment value {Value:0.##} (greedy would give {Greedy:0.##}, upper bound {Bound:0.##}, gap {Gap:0.##}%); {Nodes} search nodes; proved optimal: {Optimal}.",
                    selection.TotalValue, selection.GreedyValue, selection.UpperBound, selection.OptimalityGapPercent,
                    selection.NodesVisited, selection.ProvedOptimal);

                foreach (var route in selection.Routes)
                {
                    try
                    {
                        var created = await TryCreateAsync(route, ctx);
                        if (created != null)
                        {
                            result.RoutesCreated++;
                            result.MatchedCount += route.Plan.Stops.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Matching engine: failed saving the route for donation {DonationId}.", route.AnchorDonationId);
                        result.FailedConstraints.Add($"תרומה #{route.AnchorDonationId}: שגיאה פנימית בשמירת המסלול.");
                    }
                }

                // ---------- שלב 8: תרומות שנשארו - ניסיון נוסף עם מי שנשאר פנוי, והסלמה רק כשצריך ----------
                foreach (var entry in candidates)
                {
                    if (ctx.AssignedDonationIds.Contains(entry.Donation.Id))
                        continue;

                    try
                    {
                        var outcome = await TryMatchAsync(entry.Donation, ctx, result.FailedConstraints, hadRoutesThisRun: entry.Routes.Count > 0);
                        if (outcome.Matched)
                        {
                            result.RoutesCreated++;
                            result.MatchedCount += outcome.DonationIds.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Matching engine: failed processing donation {DonationId}.", entry.Donation.Id);
                        result.FailedConstraints.Add($"תרומה #{entry.Donation.Id}: שגיאה פנימית בעיבוד.");
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
            var outcome = await TryMatchAsync(donation, ctx, failures, hadRoutesThisRun: false);
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

            // "כל תרומה מיועדת לאותו יום בלבד" - לפי היום המקומי, לא לפי תאריך ה-UTC
            var donations = await _donationRepository.GetAvailableDonationsAsync(now, dayStartUtc);

            // תוקן: עמותה שעדיין לא אושרה יכלה לקבל שידוך
            var ngos = await _ngoRepository.GetApprovedAsync();

            return new RunContext
            {
                Now = now,
                Policy = _policyProvider.Current,
                Donations = donations,
                FailedPairs = await _matchRepository.GetFailedVolunteerDonationPairsAsync(donations.Select(d => d.Id).ToList()),
                Ngos = ngos,
                NgoRemainingKg = await _ngoSelectionService.GetRemainingCapacityKgAsync(ngos, now),
                // תוקן: מתנדב שעדיין לא אושר יכול היה לקבל שידוך.
                // רק מתנדבים שרישיון הנהיגה שלהם בתוקף היום (לפי התאריך בישראל) - DriverLicensePolicy
                Volunteers = await _volunteerRepository.GetAvailableVolunteersAsync(DriverLicensePolicy.TodayLocal(now)),
                // מתנדב שכבר יש לו הצעה או מסלול פעיל לא מקבל הצעה נוספת
                BusyVolunteerIds = await _matchRepository.GetVolunteerIdsWithActiveMatchesAsync(),
                Regions = regions,
                DonationIndex = new GeoKdTree<Donation>(donations, d => d.Latitude, d => d.Longitude)
            };
        }

        /// <summary>
        /// כל המסלולים האפשריים לתרומה (תרומת העוגן), לפי מצב ההקשר הנוכחי: עמותות, מתנדבים פנויים, תרומות נוספות.
        /// </summary>
        private async Task<DonationCandidates> BuildCandidatesAsync(Donation donation, RunContext ctx)
        {
            var policy = ctx.Policy;
            var result = new DonationCandidates { Donation = donation };

            if (donation.Status != DonationStatus.Pending)
            {
                result.Reason = "התרומה אינה ממתינה לשיבוץ.";
                result.Escalate = false;
                return result;
            }

            var deadline = NgoAvailabilityHelper.GetPickupDateTime(donation);
            if (deadline <= ctx.Now)
            {
                result.Reason = "שעת האיסוף האחרונה כבר עברה.";
                result.Escalate = false;
                return result;
            }

            // תיקון 27: גם אם המתנדב יאשר בסוף חלון התגובה, הוא צריך להספיק להגיע
            if (deadline < ctx.EarliestDepartureUtc)
            {
                result.Reason = "שעת האיסוף האחרונה קרובה מדי - אין זמן למתנדב לאשר את הבקשה ולהגיע.";
                return result;
            }

            // ---------- עמותות (תיקונים 20-21): כל מי שיכולה לקבל, לא רק אחת ----------
            var ngoOptions = _ngoSelectionService.RankNgos(donation, ctx.Ngos, ctx.NgoRemainingKg, ctx.EarliestDepartureUtc);
            if (ngoOptions.Count == 0)
            {
                result.Reason = "אין עמותה מאושרת שיכולה לקבל את התרומה בזמן (שעות קבלה / קיבולת יומית בק\"ג).";
                return result;
            }

            // ---------- מתנדבים: Hard Constraints ----------
            // מתנדב שכבר דחה את התרומה / לא ענה בזמן / לא הגיע - לא מקבל אותה שוב (ולא "מתחיל מחדש" 5 דקות)
            double weightKg = donation.TotalWeightKg();
            var pool = ctx.Volunteers
                .Where(v => !ctx.BusyVolunteerIds.Contains(v.Id) && !ctx.FailedPairs.Contains((v.Id, donation.Id)))
                .Where(v => v.MaxCapacityKg >= weightKg)
                .ToList();

            // תיקון 24: חלונות זמינות שמתחילים עד הריצה הבאה (או עד מועד האיסוף, אם הוא מוקדם יותר)
            var windowEnd = deadline < ctx.LatestDepartureUtc ? deadline : ctx.LatestDepartureUtc;
            var availability = _regionMatcher.FindAvailability(
                donation, ngoOptions.Select(o => o.Ngo).ToList(), pool, ctx.Now, ctx.EarliestDepartureUtc, windowEnd);

            if (availability.Count == 0)
                return NoRoute(result, "אין מתנדב זמין עם זמינות מתאימה (יום, שעה, אזור וכיוון נסיעה).", ngoOptions, ctx);

            // ---------- תיקון 30: נקודת יציאה + סינון בקו אווירי (KD-Tree) לפני כל פנייה ל-Google ----------
            var starts = new List<(VolunteerAvailability Availability, GeoCoordinate Start)>();
            foreach (var a in availability)
            {
                var start = ResolveStartLocation(a, ctx);
                if (start != null) starts.Add((a, start));
            }

            var startIndex = new GeoKdTree<(VolunteerAvailability Availability, GeoCoordinate Start)>(
                starts, s => s.Start.Latitude, s => s.Start.Longitude);
            var nearby = startIndex.WithinRadius(donation.Latitude, donation.Longitude, policy.MaxSearchRadiusKm);

            // ---------- מרחק נסיעה (Google, עם מטמון) מנקודת היציאה של כל מתנדב ----------
            var scored = new List<VolunteerCandidate>(nearby.Count);
            foreach (var (a, start) in nearby)
            {
                var (km, _) = await _distances.GetDrivingDistanceAsync(start.Latitude, start.Longitude, donation.Latitude, donation.Longitude);
                if (km > policy.MaxSearchRadiusKm) continue;

                double score = _rankingService.Score(a.Volunteer, donation, km, policy.MaxSearchRadiusKm, ctx.Now);
                scored.Add(new VolunteerCandidate(a.Volunteer, km, score, start, a.Window));
            }

            if (scored.Count == 0)
                return NoRoute(result, "כל המתנדבים הזמינים נמצאים מחוץ לטווח החיפוש.", ngoOptions, ctx);

            // ---------- דירוג המתנדבים (Merge Sort יציב): ציון, ואז מרחק, ואז מזהה ----------
            var ranked = CustomSortAlgorithms<VolunteerCandidate>.MergeSort(scored, (x, y) =>
            {
                int byScore = y.Score.CompareTo(x.Score);
                if (byScore != 0) return byScore;
                int byDistance = x.DistanceKm.CompareTo(y.DistanceKm);
                return byDistance != 0 ? byDistance : x.Volunteer.Id.CompareTo(y.Volunteer.Id);
            });

            // ---------- תרומות נוספות אפשריות סביב העוגן (KD-Tree) ----------
            var cluster = ctx.DonationIndex
                .WithinRadius(donation.Latitude, donation.Longitude, policy.ClusterRadiusKm)
                .Where(d => d.Id != donation.Id && !ctx.AssignedDonationIds.Contains(d.Id))
                .ToList();

            // ---------- Backtracking: מסלול לכל מתנדב לפי הדירוג, עם חלופה כשנכשל ----------
            var volunteersWithRoute = new HashSet<int>();
            var routes = await _backtracking.FindRouteCandidatesAsync(
                ranked,
                policy.MaxSearchRadiusKm,
                policy.MaxRoutesEvaluatedPerDonation,
                async c =>
                {
                    if (volunteersWithRoute.Contains(c.Volunteer.Id)) return null;   // כבר יש לו מסלול (מחלון זמינות אחר)
                    var route = await BestRouteForAsync(c, donation, cluster, ngoOptions, ctx);
                    if (route != null) volunteersWithRoute.Add(c.Volunteer.Id);
                    return route;
                });

            if (routes.Count == 0)
                return NoRoute(result,
                    "לא נמצא מסלול שעומד בכל האילוצים (זמני איסוף, שעות קבלה בעמותה, זמינות המתנדב, קיבולת הרכב וכניסת השבת).",
                    ngoOptions, ctx);

            // ---------- Matching Score לכל מסלול ----------
            ScoreRoutes(routes);
            result.Routes.AddRange(routes);
            return result;
        }

        // תיקון 20: המסלול הטוב ביותר של המתנדב לתרומה - מבין כמה עמותות (ולא עמותה אחת שנקבעה מראש)
        private async Task<RouteCandidate?> BestRouteForAsync(
            VolunteerCandidate candidate, Donation anchor, List<Donation> cluster, List<NgoOption> ngoOptions, RunContext ctx)
        {
            // תיקון 13: גם התחנות הנוספות במסלול - בלי תרומות שהמתנדב הזה כבר דחה / לא ענה עליהן
            var pool = cluster.Where(d => !ctx.FailedPairs.Contains((candidate.Volunteer.Id, d.Id))).ToList();
            var anchorRegion = new[] { anchor.RegionId };

            RouteCandidate? best = null;
            int tried = 0;
            foreach (var option in ngoOptions)
            {
                if (!AvailabilityHelper.SlotServesRoute(candidate.Window.Slot, anchorRegion, option.Ngo.RegionId))
                    continue;
                if (tried++ >= Math.Max(1, ctx.Policy.NgoCandidatesPerRoute))
                    break;

                var plan = await _orchestrator.PlanAsync(new RouteRequest
                {
                    Volunteer = candidate.Volunteer,
                    Anchor = anchor,
                    Pool = pool,
                    Ngo = option.Ngo,
                    Window = candidate.Window,
                    Start = candidate.StartLocation,
                    OfferTimeUtc = ctx.Now,
                    EarliestDepartureUtc = ctx.EarliestDepartureUtc,
                    LatestDepartureUtc = ctx.LatestDepartureUtc,
                    NgoRemainingKg = ctx.NgoRemainingKg.TryGetValue(option.Ngo.Id, out var left) ? left : double.PositiveInfinity
                });

                if (plan != null && (best == null || DonationMatchingOrchestrator.IsBetter(plan, best.Plan)))
                    best = new RouteCandidate { Candidate = candidate, Plan = plan, Ngo = option.Ngo, AnchorDonationId = anchor.Id };
            }

            return best;
        }

        // שיבוץ של תרומה אחת: הנתיב של שיבוץ ידני / שיבוץ מחדש, ושל הניסיון הנוסף אחרי הבחירה הגלובלית
        private async Task<DonationMatchOutcome> TryMatchAsync(Donation donation, RunContext ctx, List<string> failures, bool hadRoutesThisRun)
        {
            if (donation.Status != DonationStatus.Pending)
                return NotMatched(donation, "התרומה אינה ממתינה לשיבוץ.");

            // תיקון 9: גם שיבוץ ידני / שיבוץ מחדש (אחרי דחייה או אי-מענה) לא שולח הצעה בשבת, ובלי הסלמה למנהל
            if (ShabbatClock.IsShabbat(ctx.Now, ctx.Policy))
                return Fail(donation, failures, ShabbatClock.EngineMessage);

            var candidates = await BuildCandidatesAsync(donation, ctx);

            foreach (var route in candidates.Routes.OrderByDescending(r => r.Value))
            {
                var created = await TryCreateAsync(route, ctx);
                if (created == null)
                    continue;   // אחת התרומות שובצה במקביל - מנסים את המסלול הבא

                var donationIds = route.Plan.Stops.Select(s => s.DonationId).ToList();
                var anchorMatch = created.Matches?.FirstOrDefault(m => m.DonationId == donation.Id);
                return new DonationMatchOutcome(true, created.Id, anchorMatch?.Id, CreatedMessage(route, ctx), donationIds);
            }

            if (candidates.Routes.Count > 0)
                return Fail(donation, failures, "התרומה שובצה במקביל בתהליך אחר.");

            var reason = candidates.Reason ?? "לא נמצא מסלול מתאים.";
            bool nextRunInTime = NgoAvailabilityHelper.GetPickupDateTime(donation) > ctx.LatestDepartureUtc;

            // תיקון 19: היו לה מסלולים בריצה הזו, אבל המתנדבים שלהם קיבלו תרומות אחרות (הבחירה הגלובלית) -
            // הריצה הבאה תבדוק אותה שוב (אלא אם הריצה הבאה כבר מאוחרת מדי)
            if (hadRoutesThisRun && nextRunInTime)
                return Fail(donation, failures, "המתנדבים שמתאימים לה שובצו בריצה הזו לתרומות אחרות; התרומה תיבדק שוב בריצה הבאה.");

            // תיקון 25: יש מתנדבים במשמרת מאוחרת יותר היום, לפני מועד האיסוף - אין סיבה להקפיץ מנהל עכשיו
            if (!candidates.Escalate)
                return Fail(donation, failures, candidates.HasLaterAvailability
                    ? $"{reason} יש מתנדבים במשמרת מאוחרת יותר היום, לפני מועד האיסוף - לא נשלחה התראה למנהל."
                    : reason);

            return await EscalateAsync(donation, failures, reason);
        }

        // אין מסלול לתרומה כרגע. מסלימים למנהל רק אם גם בהמשך היום אין מי שיכול לקחת אותה.
        private static DonationCandidates NoRoute(DonationCandidates result, string reason, List<NgoOption> ngoOptions, RunContext ctx)
        {
            result.Reason = reason;
            result.HasLaterAvailability = HasLaterAvailability(result.Donation, ngoOptions, ctx);
            result.Escalate = !result.HasLaterAvailability;
            return result;
        }

        /// <summary>
        /// תיקון 25: האם יש מתנדב (גם אם יש לו עכשיו הצעה פתוחה) עם משמרת מתאימה אחרי הריצה הבאה ולפני מועד האיסוף.
        /// אם הריצה הבאה כבר אחרי מועד האיסוף - זו ההזדמנות האחרונה, ולכן false (ההסלמה נשלחת עכשיו).
        /// </summary>
        private static bool HasLaterAvailability(Donation donation, List<NgoOption> ngoOptions, RunContext ctx)
        {
            var deadline = NgoAvailabilityHelper.GetPickupDateTime(donation);
            if (deadline <= ctx.LatestDepartureUtc || ngoOptions.Count == 0)
                return false;

            double weightKg = donation.TotalWeightKg();
            var donationRegion = new[] { donation.RegionId };
            var ngoRegions = ngoOptions.Select(o => o.Ngo.RegionId).Distinct().ToList();

            return ctx.Volunteers.Any(v =>
                v.MaxCapacityKg >= weightKg &&
                !ctx.FailedPairs.Contains((v.Id, donation.Id)) &&
                AvailabilityHelper.GetWindows(v, ctx.LatestDepartureUtc, deadline).Any(w =>
                    w.EndUtc > ctx.LatestDepartureUtc &&
                    ngoRegions.Any(r => AvailabilityHelper.SlotServesRoute(w.Slot, donationRegion, r))));
        }

        // שמירת מסלול שנבחר. null = לא נשמר (מתנדב / תרומה כבר נלקחו, או שינוי מקביל במסד).
        private async Task<VolunteerRoute?> TryCreateAsync(RouteCandidate route, RunContext ctx)
        {
            var volunteer = route.Candidate.Volunteer;
            if (ctx.BusyVolunteerIds.Contains(volunteer.Id) ||
                route.Plan.Stops.Any(s => ctx.AssignedDonationIds.Contains(s.DonationId)))
                return null;

            var created = await _matchCreationService.CreateRouteAsync(volunteer, route.Ngo, route.Plan, route.Score);
            if (created == null)
                return null;

            ctx.BusyVolunteerIds.Add(volunteer.Id);
            foreach (var stop in route.Plan.Stops)
                ctx.AssignedDonationIds.Add(stop.DonationId);
            if (ctx.NgoRemainingKg.TryGetValue(route.Ngo.Id, out var left))
                ctx.NgoRemainingKg[route.Ngo.Id] = Math.Max(0, left - route.Plan.TotalWeightKg);

            _logger.LogInformation(
                "Matching engine: route {RouteId} ({Stops} stops, {Kg:0.#} kg, score {Score:0.0}, departure {Departure:HH:mm} local) offered to volunteer {VolunteerId}, NGO {NgoId}.",
                created.Id, route.Plan.Stops.Count, route.Plan.TotalWeightKg, route.Score,
                LocalClock.ToLocal(route.Plan.StartTime), volunteer.Id, route.Ngo.Id);

            return created;
        }

        private static string CreatedMessage(RouteCandidate route, RunContext ctx)
        {
            var message = $"נוצר מסלול עם {route.Plan.Stops.Count} תחנות ונשלחה בקשה למתנדב.";
            return route.Plan.StartTime > ctx.EarliestDepartureUtc.AddMinutes(1)
                ? $"{message} יציאה מתוכננת ב-{LocalClock.ToLocal(route.Plan.StartTime):HH:mm}."
                : message;
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

        // נקודת היציאה: מיקום GPS עדכני, ואם אין - מרכז אזור המוצא של חלון הזמינות שבו המתנדב ייסע
        private static GeoCoordinate? ResolveStartLocation(VolunteerAvailability availability, RunContext ctx)
        {
            var v = availability.Volunteer;
            bool hasFreshGps = (v.CurrentLatitude != 0 || v.CurrentLongitude != 0) &&
                               v.LocationUpdatedAt.HasValue &&
                               (ctx.Now - v.LocationUpdatedAt.Value).TotalMinutes <= ctx.Policy.MaxLocationAgeMinutes;

            if (hasFreshGps)
                return new GeoCoordinate { Latitude = v.CurrentLatitude, Longitude = v.CurrentLongitude };

            if (ctx.Regions.TryGetValue(availability.Window.Slot.RegionId, out var region))
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

            return Fail(donation, failures, reason);
        }

        private static DonationMatchOutcome Fail(Donation donation, List<string> failures, string reason)
        {
            failures.Add($"תרומה #{donation.Id}: {reason}");
            return NotMatched(donation, reason);
        }

        private static DonationMatchOutcome NotMatched(Donation donation, string reason) =>
            new(false, null, null, reason, Array.Empty<int>());
    }
}
