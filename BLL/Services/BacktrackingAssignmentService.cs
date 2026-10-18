using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>מתנדב מועמד יחד עם המרחק שלו לתרומה וציון ההתאמה שלו.</summary>
    public class VolunteerCandidate
    {
        public Volunteer Volunteer { get; }
        public double DistanceKm { get; }
        public double Score { get; }
        public GeoCoordinate StartLocation { get; }

        public VolunteerCandidate(Volunteer volunteer, double distanceKm, double score = 0, GeoCoordinate? startLocation = null)
        {
            Volunteer = volunteer ?? throw new ArgumentNullException(nameof(volunteer));
            DistanceKm = distanceKm;
            Score = score;
            StartLocation = startLocation ?? new GeoCoordinate();
        }
    }

    /// <summary>מסלול שנבנה בהצלחה למתנדב מועמד</summary>
    public sealed class RouteCandidate
    {
        public VolunteerCandidate Candidate { get; init; } = null!;
        public RoutePlan Plan { get; init; } = null!;

        // Matching Score סופי של המסלול (0-100)
        public double Score { get; set; }
    }

    /// <summary>
    /// מעבר רקורסיבי (Backtracking) על רשימת המועמדים המדורגת: לכל מועמד מנסים לבנות מסלול; אם אי אפשר
    /// (אילוץ קשה נכשל) - חוזרים אחורה וממשיכים למועמד הבא. מועמד שמרחקו חורג מטווח החיפוש נגזם (Bound)
    /// בלי לבנות לו מסלול. נעצרים כשנמצאו maxRoutes מסלולים אפשריים - מתוכם נבחר המסלול עם הציון הגבוה.
    ///
    /// תוקן: בעבר זה היה מסלול שידוך נפרד (עם כללים משלו, ושנוצר דרכו שידוך נוסף מתוך Program.cs).
    /// עכשיו זה רכיב חיפוש בלבד בתוך המנוע היחיד (DonationMatchingBatchService) - לא יוצר שידוכים בעצמו.
    /// </summary>
    public class BacktrackingAssignmentService
    {
        public Task<List<RouteCandidate>> FindRoutesAsync(
            IReadOnlyList<VolunteerCandidate> rankedCandidates,
            double maxDistanceKm,
            int maxRoutes,
            Func<VolunteerCandidate, Task<RoutePlan?>> tryBuildRoute)
        {
            if (rankedCandidates == null) throw new ArgumentNullException(nameof(rankedCandidates));
            if (tryBuildRoute == null) throw new ArgumentNullException(nameof(tryBuildRoute));

            return SearchAsync(rankedCandidates, 0, Math.Max(1, maxRoutes), maxDistanceKm, tryBuildRoute, new List<RouteCandidate>());
        }

        private static async Task<List<RouteCandidate>> SearchAsync(
            IReadOnlyList<VolunteerCandidate> candidates,
            int index,
            int maxRoutes,
            double maxDistanceKm,
            Func<VolunteerCandidate, Task<RoutePlan?>> tryBuildRoute,
            List<RouteCandidate> found)
        {
            // תנאי עצירה: עברנו על כל המועמדים, או שנמצאו מספיק מסלולים להשוואה
            if (index >= candidates.Count || found.Count >= maxRoutes)
                return found;

            var candidate = candidates[index];

            // Bound: מועמד מחוץ לטווח - לא בונים לו מסלול בכלל
            if (candidate.DistanceKm <= maxDistanceKm)
            {
                var plan = await tryBuildRoute(candidate);
                if (plan != null)
                    found.Add(new RouteCandidate { Candidate = candidate, Plan = plan });
            }

            // Backtrack: ממשיכים למועמד הבא
            return await SearchAsync(candidates, index + 1, maxRoutes, maxDistanceKm, tryBuildRoute, found);
        }
    }
}
