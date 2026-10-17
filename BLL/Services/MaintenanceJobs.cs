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
        private readonly IDonationRepository _donationRepository;
        private readonly IActionTokenRepository _actionTokenRepository;
        private readonly ILoginAttemptRepository _loginAttemptRepository;
        private readonly RouteWorkflowService _workflow;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<MaintenanceJobs> _logger;

        public MaintenanceJobs(
            IDonationRepository donationRepository,
            IActionTokenRepository actionTokenRepository,
            ILoginAttemptRepository loginAttemptRepository,
            RouteWorkflowService workflow,
            IUnitOfWork unitOfWork,
            ILogger<MaintenanceJobs> logger)
        {
            _donationRepository = donationRepository;
            _actionTokenRepository = actionTokenRepository;
            _loginAttemptRepository = loginAttemptRepository;
            _workflow = workflow;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        // תוקן: תרומה שפג תוקפה נשארה Pending לנצח (והסטטוס Expired לא נקבע אף פעם).
        // עכשיו: תרומה שפגה ועדיין לא נאספה מסומנת Expired, ושידוך פעיל שלה (שעוד לא נאסף) מבוטל.
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        [AutomaticRetry(Attempts = 0)]
        public async Task<int> ExpireDonationsAsync()
        {
            var now = DateTime.UtcNow;
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

        // ניקוי טוקנים חד-פעמיים ישנים וניסיונות התחברות ישנים
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        [AutomaticRetry(Attempts = 0)]
        public async Task CleanupAsync()
        {
            var now = DateTime.UtcNow;
            await _actionTokenRepository.DeleteExpiredAsync(now.AddDays(-7));
            await _loginAttemptRepository.DeleteOlderThanAsync(now.AddDays(-30));
        }
    }
}
