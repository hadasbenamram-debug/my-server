using BLL.Services;
using System;
using System.Collections.Generic;
using BLL.Configuration;
using DAL.Classes;

namespace BLL.Algorithms
{
    /// <summary>
    /// מחשב ציון התאמה משוקלל למתנדב מול תרומה נתונה, על בסיס:
    /// מרחק, דחיפות התרומה, קיבולת רכב פנויה, וציון אמינות המתנדב.
    ///
    /// הערה על distanceKm: הפרמטר הזה מקורו ב-Google Distance Matrix API
    /// (מרחק/זמן נסיעה אמיתי בכביש) ולא בקירוב גס - ראו GoogleMapsIntegrationService.
    ///
    /// תיקון: רכיב הדחיפות (Urgency) חושב בעבר ישירות מ-donation.UrgencyScore - שדה
    /// מאוחסן ב-DB שמתעדכן (כמו PriorityScore) רק כל כמה דקות ע"י ה-Scheduler, ולכן
    /// יכול להיות מיושן ברגע קבלת ההחלטה בפועל, ולא לוקח בחשבון את מידת המתכלות
    /// (PerishabilityFactor) של סוג המזון. כעת נעשה שימוש ב-PriorityCalculator, שמחשב
    /// דחיפות בזמן אמת מתוך הזמן שנותר בפועל וממידת המתכלות - אותו מקור שממנו
    /// ניזון גם KnapsackSolver, כך שההגדרה של "דחיפות" עקבית בכל המערכת.
    /// </summary>
    public class VolunteerMatchScorer
    {
        private readonly IMatchingPolicyProvider _policy;

        public VolunteerMatchScorer(IMatchingPolicyProvider? policy = null)
        {
            _policy = policy ?? new StaticMatchingPolicyProvider();
        }

        /// <summary>
        /// מחזיר ציון בין 0 ל-1 (גבוה יותר = התאמה טובה יותר).
        /// כל רכיב מנורמל לטווח 0-1 לפני השקלול. המשקלות נלקחים מהמדיניות (ניתנים לשינוי ב-api/settings),
        /// ומנורמלים כך שסכומם 1.
        /// </summary>
        public double CalculateScore(
            Volunteer volunteer,
            Donation donation,
            double distanceKm,
            double maxSearchRadiusKm,
            DateTime? asOfUtc = null,
            double? urgencyTimeConstantMinutes = null) =>
            CalculateBreakdown(volunteer, donation, distanceKm, maxSearchRadiusKm, asOfUtc, urgencyTimeConstantMinutes).Total;

        /// <summary>
        /// תיקון 75: אותו חישוב בדיוק, עם כל רכיב בנפרד (0-1, לפני שקלול) - בשביל ההסבר "למה המנוע החליט כך".
        /// </summary>
        public ScoreBreakdown CalculateBreakdown(
            Volunteer volunteer,
            Donation donation,
            double distanceKm,
            double maxSearchRadiusKm,
            DateTime? asOfUtc = null,
            double? urgencyTimeConstantMinutes = null)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (donation == null) throw new ArgumentNullException(nameof(donation));

            var p = _policy.Current;
            double totalWeight = Math.Max(1e-9, p.DistanceWeight + p.UrgencyWeight + p.CapacityWeight + p.ReliabilityWeight);

            double normalizedDistance = 1.0 - Math.Clamp(distanceKm / Math.Max(maxSearchRadiusKm, 0.001), 0, 1);

            double normalizedUrgency = Math.Clamp(
                PriorityCalculator.Calculate(donation, asOfUtc ?? DateTime.UtcNow, urgencyTimeConstantMinutes ?? p.UrgencyTimeConstantMinutes) / 100.0,
                0, 1);

            // קיבולת פנויה ביחס למשקל התרומה (קיבולת כפולה ומעלה = ציון מלא)
            double capacityRatio = volunteer.MaxCapacityKg <= 0
                ? 0
                : Math.Clamp(volunteer.MaxCapacityKg / Math.Max(donation.TotalWeightKg(), 0.001), 0, 2) / 2.0;

            // נורמליזציה של ציון האמינות לפי סקאלה של 1 עד 5
            double normalizedReliability = Math.Clamp(volunteer.ReliabilityScore / 5.0, 0, 1);

            double total = ((normalizedDistance * p.DistanceWeight)
                 + (normalizedUrgency * p.UrgencyWeight)
                 + (capacityRatio * p.CapacityWeight)
                 + (normalizedReliability * p.ReliabilityWeight)) / totalWeight;

            return new ScoreBreakdown(normalizedDistance, normalizedUrgency, capacityRatio, normalizedReliability, total);
        }
    }

    /// <summary>תיקון 75: רכיבי ציון ההתאמה (כל אחד 0-1) והציון המשוקלל (0-1)</summary>
    public sealed record ScoreBreakdown(double Distance, double Urgency, double Capacity, double Reliability, double Total);

    /// <summary>
    /// מימוש עצמאי (לא שימוש ב-List.Sort המובנה) של Merge Sort ו-Quick Sort,
    /// גנרי - כדי שיהיה ניתן להשתמש בו לכל T, לא רק למתנדבים.
    ///
    /// Merge Sort: יציב, O(n log n) תמיד. שופר: מערך עזר אחד לכל המיון (O(n) זיכרון) - בעבר כל רמה ברקורסיה
    /// יצרה רשימות חדשות (GetRange) - O(n log n) הקצאות.
    /// Quick Sort: Median-of-three + חלוקה לשלושה (קטן / שווה / גדול, "הדגל ההולנדי"). שופר: בחלוקה הקודמת
    /// (Lomuto עם &lt;=) רשימה עם הרבה ערכים שווים - למשל ציונים זהים - התדרדרה ל-O(n²). עכשיו O(n log n) בממוצע,
    /// ועומק רקורסיה O(log n) (רקורסיה רק על החלק הקטן).
    /// </summary>
    public static class CustomSortAlgorithms<T>
    {
        public static List<T> MergeSort(List<T> items, Comparison<T> comparer)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));

            var array = items.ToArray();
            if (array.Length > 1)
                MergeSortRange(array, new T[array.Length], 0, array.Length, comparer);
            return new List<T>(array);
        }

        // ממיין את [from, to) במקום, בעזרת מערך העזר buffer
        private static void MergeSortRange(T[] items, T[] buffer, int from, int to, Comparison<T> comparer)
        {
            if (to - from <= 1) return;

            int mid = from + (to - from) / 2;
            MergeSortRange(items, buffer, from, mid, comparer);
            MergeSortRange(items, buffer, mid, to, comparer);

            // החצאים כבר בסדר הנכון - אין מה למזג
            if (comparer(items[mid - 1], items[mid]) <= 0) return;

            Array.Copy(items, from, buffer, from, to - from);
            int i = from, j = mid, k = from;
            while (i < mid && j < to)
                items[k++] = comparer(buffer[i], buffer[j]) <= 0 ? buffer[i++] : buffer[j++];   // <= שומר על יציבות
            while (i < mid) items[k++] = buffer[i++];
            while (j < to) items[k++] = buffer[j++];
        }

        public static void QuickSort(List<T> items, Comparison<T> comparer)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));
            QuickSortRange(items, 0, items.Count - 1, comparer);
        }

        private static void QuickSortRange(List<T> items, int low, int high, Comparison<T> comparer)
        {
            while (low < high)
            {
                // הציר: החציון מבין ראשון / אמצעי / אחרון
                int mid = low + (high - low) / 2;
                MedianOfThree(items, low, mid, high, comparer);
                T pivot = items[mid];

                // חלוקה לשלושה: [low, lt) קטנים, [lt, gt] שווים לציר, (gt, high] גדולים
                int lt = low, i = low, gt = high;
                while (i <= gt)
                {
                    int c = comparer(items[i], pivot);
                    if (c < 0) Swap(items, lt++, i++);
                    else if (c > 0) Swap(items, i, gt--);
                    else i++;
                }

                // רקורסיה על החלק הקטן, לולאה על הגדול - עומק מחסנית O(log n)
                if (lt - low < high - gt)
                {
                    QuickSortRange(items, low, lt - 1, comparer);
                    low = gt + 1;
                }
                else
                {
                    QuickSortRange(items, gt + 1, high, comparer);
                    high = lt - 1;
                }
            }
        }

        private static void MedianOfThree(List<T> items, int low, int mid, int high, Comparison<T> comparer)
        {
            if (comparer(items[mid], items[low]) < 0) Swap(items, low, mid);
            if (comparer(items[high], items[low]) < 0) Swap(items, low, high);
            if (comparer(items[high], items[mid]) < 0) Swap(items, mid, high);
        }

        private static void Swap(List<T> items, int i, int j)
        {
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// שירות ה-BLL שמחבר בין החישוב (VolunteerMatchScorer) לבין המיון (CustomSortAlgorithms).
    /// </summary>
    public class VolunteerRankingService
    {
        private readonly VolunteerMatchScorer _scorer;

        public VolunteerRankingService(VolunteerMatchScorer scorer)
        {
            _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        }

        public double Score(Volunteer volunteer, Donation donation, double distanceKm, double maxSearchRadiusKm, DateTime? asOfUtc = null) =>
            _scorer.CalculateScore(volunteer, donation, distanceKm, maxSearchRadiusKm, asOfUtc);

        // תיקון 75: הציון מפורק לרכיבים (להסבר ההחלטה)
        public ScoreBreakdown Breakdown(Volunteer volunteer, Donation donation, double distanceKm, double maxSearchRadiusKm, DateTime? asOfUtc = null) =>
            _scorer.CalculateBreakdown(volunteer, donation, distanceKm, maxSearchRadiusKm, asOfUtc);

        public List<Volunteer> RankVolunteers(
            List<Volunteer> availableVolunteers,
            Donation donation,
            Dictionary<int, double> distanceByVolunteerId,
            double maxSearchRadiusKm,
            bool useQuickSort = false,
            DateTime? asOfUtc = null)
        {
            if (availableVolunteers == null || availableVolunteers.Count == 0)
                return new List<Volunteer>();

            var scores = new Dictionary<int, double>();
            foreach (var v in availableVolunteers)
            {
                double distance = distanceByVolunteerId.TryGetValue(v.Id, out var d) ? d : maxSearchRadiusKm;
                scores[v.Id] = _scorer.CalculateScore(v, donation, distance, maxSearchRadiusKm, asOfUtc);
            }

            Comparison<Volunteer> byScoreDescending = (a, b) => scores[b.Id].CompareTo(scores[a.Id]);

            if (useQuickSort)
            {
                var copy = new List<Volunteer>(availableVolunteers);
                CustomSortAlgorithms<Volunteer>.QuickSort(copy, byScoreDescending);
                return copy;
            }

            return CustomSortAlgorithms<Volunteer>.MergeSort(availableVolunteers, byScoreDescending);
        }
    }
}
