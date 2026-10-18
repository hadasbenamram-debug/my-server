using BLL.Configuration;
using DAL.Interfaces;
using Hangfire;
using System;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// Hangfire job: מרענן את donation.PriorityScore במסד (לתצוגה ולמיון במסכי הניהול), לפי אותה נוסחה
    /// שהמנוע משתמש בה (PriorityCalculator). המנוע עצמו מחשב דחיפות בזמן אמת ולא תלוי בערך השמור.
    /// תוקן: PriorityUpdatedAt לא עודכן אף פעם.
    /// </summary>
    public class PriorityDecayScheduler
    {
        private const double MinDeltaToPersist = 0.5;

        private readonly IDonationRepository _donationRepository;
        private readonly IMatchingPolicyProvider _policy;

        public PriorityDecayScheduler(IDonationRepository donationRepository, IMatchingPolicyProvider policy)
        {
            _donationRepository = donationRepository;
            _policy = policy;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        [AutomaticRetry(Attempts = 0)]
        public async Task ProcessPriorityDecay()
        {
            var now = DateTime.UtcNow;
            var dayStartUtc = LocalClock.ToUtc(LocalClock.ToLocal(now).Date);
            var activeDonations = await _donationRepository.GetAvailableDonationsAsync(now, dayStartUtc);
            var timeConstant = _policy.Current.UrgencyTimeConstantMinutes;

            bool anyChanged = false;

            foreach (var donation in activeDonations)
            {
                double newPriority = PriorityCalculator.Calculate(donation, now, timeConstant);

                if (Math.Abs(donation.PriorityScore - newPriority) < MinDeltaToPersist)
                    continue;

                donation.PriorityScore = newPriority;
                donation.PriorityUpdatedAt = now;
                anyChanged = true;
            }

            if (anyChanged)
                await _donationRepository.SaveChangesAsync();
        }
    }
}
