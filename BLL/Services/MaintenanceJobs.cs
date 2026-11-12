using BLL.Algorithms;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Services
{
    // ג'ובים תקופתיים לניקיון ולסטטוסים שלא עודכנו אף פעם.
    public class MaintenanceJobs
    {
        // מיקומי GPS של מתנדבים (יומן המשלוח) נשמרים 30 יום. האירועים במסלול (איסוף, מסירה...) נשארים.
        public const int LocationRetentionDays = 30;

        private readonly IDonationRepository _donationRepository;
        private readonly IActionTokenRepository _actionTokenRepository;
        private readonly ILoginAttemptRepository _loginAttemptRepository;
        private readonly RouteWorkflowService _workflow;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<MaintenanceJobs> _logger;
        private readonly IDeliveryLogRepository _deliveryLogRepository;
        private readonly TimeProvider _clock;   // שעון אחד לכל המערכת (בבדיקות - שעון קבוע)

        public MaintenanceJobs(
            IDonationRepository donationRepository,
            IActionTokenRepository actionTokenRepository,
            ILoginAttemptRepository loginAttemptRepository,
            RouteWorkflowService workflow,
            IUnitOfWork unitOfWork,
            ILogger<MaintenanceJobs> logger,
            IDeliveryLogRepository deliveryLogRepository,
            TimeProvider? clock = null)
        {
            _deliveryLogRepository = deliveryLogRepository;
            _donationRepository = donationRepository;
            _actionTokenRepository = actionTokenRepository;
            _loginAttemptRepository = loginAttemptRepository;
            _workflow = workflow;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        // תוקן: תרומה שפג תוקפה נשארה Pending לנצח (והסטטוס Expired לא נקבע אף פעם).
        // עכשיו: תרומה שפגה ועדיין לא נאספה מסומנת Expired, ושידוך פעיל שלה (שעוד לא נאסף) מבוטל.
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        [AutomaticRetry(Attempts = 0)]
        public async Task<int> ExpireDonationsAsync()
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var expired = await _donationRepository.GetExpiredOpenAsync(now);
            int count = 0;

            foreach (var donation in expired)
            {
                try
                {
                    await _unitOfWork.ExecuteInTransactionAsync(async () =>
                    {
                        if (donation.Status == DonationStatus.Matched)
                            await _workflow.CancelActiveMatchForDonationAsync(donation, "פג תוקף התרומה לפני האיסוף.");

                        donation.Status = DonationStatus.Expired;
                        await _donationRepository.UpdateAsync(donation);
                    });
                    count++;
                }
                catch (Exception ex)
                {
                    // למשל: התרומה נאספה ברגע זה - לא מסמנים אותה כפגה
                    _logger.LogWarning(ex, "Could not expire donation {DonationId}.", donation.Id);
                }
            }

            if (count > 0)
                _logger.LogInformation("Expired {Count} donations.", count);

            return count;
        }

        // ניקוי טוקנים חד-פעמיים ישנים, ניסיונות התחברות ישנים, ומיקומי GPS ישנים מיומן המשלוח
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        [AutomaticRetry(Attempts = 0)]
        public async Task CleanupAsync()
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            await _actionTokenRepository.DeleteExpiredAsync(now.AddDays(-7));
            await _loginAttemptRepository.DeleteOlderThanAsync(now.AddDays(-30));

            var locations = await _deliveryLogRepository.DeleteLocationsOlderThanAsync(now.AddDays(-LocationRetentionDays));
            if (locations > 0)
                _logger.LogInformation("Deleted {Count} old location rows from the delivery log.", locations);
        }
    }
}
