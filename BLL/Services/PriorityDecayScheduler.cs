using BLL.Configuration;
using BLL.Interfaces;
using DAL.Classes;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// Hangfire job (כל 2 דקות): מרענן את donation.PriorityScore ב-DB (לצורך תצוגה/מיון
    /// במסכי אדמין וכד') כך שיישאר עקבי עם הדחיפות ש-DonationMatchingBatchService משתמש
    /// בה בפועל בזמן שידוך.
    ///
    /// שני תיקונים לעומת הגרסה המקורית:
    /// 1. הנוסחה הוחלפה לאותה נוסחה בדיוק שמפעיל PriorityCalculator (המקור היחיד לאמת
    ///    לגבי "דחיפות" בכל המערכת - ר' הערה ב-PriorityCalculator.cs). בגרסה הקודמת היו
    ///    שתי נוסחאות שונות: זו כאן (ליניארית-בריבוע, יחסית לאורך חלון התרומה) וזו של
    ///    ה-Knapsack/הדירוג (שהוחלפה כעת ל-PriorityCalculator) - כך שהציון שנשמר ב-DB
    ///    לא בהכרח שיקף את מה שבאמת קבע את סדר השידוך.
    /// 2. הוסרה לגמרי ההתערבות הישירה בערימה (PriorityHeapService, ר' UpdatePriority/
    ///    Remove שהוסרו). PriorityHeapService היה רשום כ-Singleton חלקית בגלל שהג'וב הזה
    ///    (רץ כל 2 דקות) והג'וב ההמוני של השידוך (רץ פעם בשעה, ממלא ומרוקן את הערימה
    ///    מתוך ריצה יחידה) כתבו במקביל לאותו מופע, מה שיצר תלות ב-DbContext scopes שונים
    ///    וסיכון ל"ישויות תקועות" (stale entities). מכיוון שדחיפות מחושבת כעת בזמן אמת
    ///    (PriorityCalculator) בכל שימוש, אין יותר צורך שהג'וב הזה יגע בערימה בכלל - וזה
    ///    מאפשר לרשום את PriorityHeapService כ-Scoped במקום Singleton (ר' Program.cs).
    /// </summary>
    public class PriorityDecayScheduler
    {
        private readonly IDonationRepository _donationRepository;
        private readonly MatchingPolicyOptions _policy;

        public PriorityDecayScheduler(
            IDonationRepository donationRepository,
            IOptions<MatchingPolicyOptions> policy = null)
        {
            _donationRepository = donationRepository;
            _policy = policy?.Value ?? new MatchingPolicyOptions();
        }

        public async Task ProcessPriorityDecay()
        {
            var activeDonations = await _donationRepository.GetAvailableDonationsAsync();
            var now = DateTime.UtcNow;

            const double MinDeltaToPersist = 0.5;
            bool anyChanged = false;

            foreach (var donation in activeDonations)
            {
                double newPriority = PriorityCalculator.Calculate(donation, now, _policy.UrgencyTimeConstantMinutes);

                if (Math.Abs(donation.PriorityScore - newPriority) < MinDeltaToPersist)
                    continue;

                donation.PriorityScore = newPriority;
                anyChanged = true;
            }

            if (anyChanged)
            {
                await _donationRepository.SaveChangesAsync();
            }
        }
    }
}
