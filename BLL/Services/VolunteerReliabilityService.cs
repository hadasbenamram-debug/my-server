using BLL.Configuration;
using DAL.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // ציון האמינות של המתנדב (1-5). תוקן: הציון לא השתנה אף פעם - הדירוג במשוב נשמר בלי השפעה,
    // ו-No-Show לא היה ממומש. עכשיו הציון מחושב מחדש אחרי כל משוב ואחרי כל No-Show:
    //   ממוצע הדירוגים "מוחלק" לעבר ציון התחלתי (כדי שמשוב אחד לא יקבע הכל),
    //   פחות קנס על כל מסלול שהסתיים ב-No-Show (MatchingPolicy:NoShowReliabilityPenalty).
    public class VolunteerReliabilityService
    {
        public const double PriorScore = 3.5;
        public const double PriorWeight = 3.0;

        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IFeedbackRepository _feedbackRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IMatchingPolicyProvider _policy;

        public VolunteerReliabilityService(
            IVolunteerRepository volunteerRepository,
            IFeedbackRepository feedbackRepository,
            IMatchRepository matchRepository,
            IMatchingPolicyProvider policy)
        {
            _volunteerRepository = volunteerRepository;
            _feedbackRepository = feedbackRepository;
            _matchRepository = matchRepository;
            _policy = policy;
        }

        public static double Calculate(System.Collections.Generic.IReadOnlyCollection<int> ratings, int noShowRoutes, double noShowPenalty)
        {
            double sum = ratings.Where(r => r >= 1 && r <= 5).Sum();
            int n = ratings.Count(r => r >= 1 && r <= 5);
            double average = (PriorScore * PriorWeight + sum) / (PriorWeight + n);
            double score = average - noShowRoutes * Math.Max(0, noShowPenalty);
            return Math.Round(Math.Clamp(score, 1.0, 5.0), 2);
        }

        public async Task<double?> RecalculateAsync(int volunteerId)
        {
            var volunteer = await _volunteerRepository.GetByIdAsync(volunteerId);
            if (volunteer == null)
                return null;

            var ratings = await _feedbackRepository.GetVolunteerRatingsAsync(volunteerId);
            var noShows = await _matchRepository.CountNoShowRoutesForVolunteerAsync(volunteerId);

            volunteer.ReliabilityScore = Calculate(ratings, noShows, _policy.Current.NoShowReliabilityPenalty);
            await _volunteerRepository.UpdateAsync(volunteer);
            return volunteer.ReliabilityScore;
        }
    }
}
