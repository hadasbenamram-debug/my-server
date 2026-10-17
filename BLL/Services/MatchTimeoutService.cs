using BLL.Configuration;
using BLL.Services;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// Hangfire jobs:
    ///   * ProcessTimeoutsAsync (כל דקה + בדיוק בתום כל חלון תגובה): בקשות שהמתנדב לא ענה עליהן בזמן
    ///     מסומנות TimedOut והתרומות עוברות מיד לשיבוץ מחדש (למתנדב הבא).
    ///   * ProcessNoShowsAsync (כל 5 דקות, רק אם MatchingPolicy:AutoNoShowGraceMinutes &gt; 0): שיבוץ שאושר
    ///     ולא נאסף עד ETA + זמן חסד מסומן No-Show.
    ///
    /// תוקן: הג'וב קיבל בעבר את DateTime.UtcNow כפרמטר - ו-Hangfire שומר את ערך הפרמטר ברגע הרישום,
    /// כך שהבדיקה תמיד רצה "בזמן העבר" (זמן עליית השרת) ולא מצאה אף שידוך שפג. עכשיו הזמן נקבע בזמן הריצה.
    /// </summary>
    public class MatchTimeoutService
    {
        private readonly IMatchRepository _matchRepository;
        private readonly RouteWorkflowService _workflow;
        private readonly IMatchingPolicyProvider _policy;
        private readonly ILogger<MatchTimeoutService> _logger;

        public MatchTimeoutService(
            IMatchRepository matchRepository,
            RouteWorkflowService workflow,
            IMatchingPolicyProvider policy,
            ILogger<MatchTimeoutService> logger)
        {
            _matchRepository = matchRepository ?? throw new ArgumentNullException(nameof(matchRepository));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 0)]
        public Task ProcessTimeoutsAsync() => ProcessTimeoutsAtAsync(DateTime.UtcNow);

        // תאימות לג'ובים שתוזמנו בגרסה הקודמת (עם פרמטר זמן). הערך הישן מתעלם - משתמשים בזמן הנוכחי.
        [Obsolete("Kept only so jobs scheduled by the previous version still run.")]
        [AutomaticRetry(Attempts = 0)]
        public Task ProcessTimeoutsAndNoShowsAsync(DateTime ignoredScheduledTime) => ProcessTimeoutsAsync();

        public async Task<int> ProcessTimeoutsAtAsync(DateTime asOfUtc)
        {
            var expiredMatches = await _matchRepository.GetExpiredAwaitingConfirmationAsync(asOfUtc);
            if (expiredMatches.Count == 0)
                return 0;

            int processed = 0;

            // מסלול שלם פג יחד - מטפלים פעם אחת לכל מסלול
            foreach (var match in expiredMatches.GroupBy(m => m.VolunteerRouteId ?? -m.Id).Select(g => g.First()))
            {
                try
                {
                    await _workflow.DeclineAsync(match, match.VolunteerId, MatchStatus.TimedOut);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MatchTimeoutService: failed to time out match {MatchId}.", match.Id);
                }
            }

            _logger.LogInformation("MatchTimeoutService: {Count} expired volunteer requests were released for re-assignment.", processed);
            return processed;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        [AutomaticRetry(Attempts = 0)]
        public Task ProcessNoShowsAsync() => ProcessNoShowsAtAsync(DateTime.UtcNow);

        public async Task<int> ProcessNoShowsAtAsync(DateTime asOfUtc)
        {
            int grace = _policy.Current.AutoNoShowGraceMinutes;
            if (grace <= 0)
                return 0;

            var overdue = await _matchRepository.GetOverdueNotPickedUpAsync(asOfUtc.AddMinutes(-grace));
            int processed = 0;

            foreach (var match in overdue.GroupBy(m => m.VolunteerRouteId ?? -m.Id).Select(g => g.First()))
            {
                try
                {
                    var full = await _matchRepository.GetByIdWithDetailsAsync(match.Id);
                    if (full == null) continue;
                    await _workflow.MarkNoShowAsync(full, "לא נאסף בזמן", automatic: true);
                    processed++;
                }
                catch (ArgumentException)
                {
                    // בינתיים נאסף / השתנה - אין מה לעשות
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MatchTimeoutService: failed to mark match {MatchId} as No-Show.", match.Id);
                }
            }

            return processed;
        }
    }
}
