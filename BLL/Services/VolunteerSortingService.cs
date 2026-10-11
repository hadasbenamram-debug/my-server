using System;
using System.Collections.Generic;
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
        public double DistanceWeight { get; set; } = 0.35;
        public double UrgencyWeight { get; set; } = 0.30;
        public double CapacityWeight { get; set; } = 0.15;
        public double ReliabilityWeight { get; set; } = 0.20;

        /// <summary>
        /// מחזיר ציון גבוה יותר = התאמה טובה יותר.
        /// כל רכיב מנורמל לטווח 0-1 לפני השקלול.
        /// </summary>
        public double CalculateScore(
            Volunteer volunteer,
            Donation donation,
            double distanceKm,
            double maxSearchRadiusKm,
            DateTime? asOfUtc = null,
            double urgencyTimeConstantMinutes = 180.0)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (donation == null) throw new ArgumentNullException(nameof(donation));

            double normalizedDistance = 1.0 - Math.Clamp(distanceKm / Math.Max(maxSearchRadiusKm, 0.001), 0, 1);

            double normalizedUrgency = Math.Clamp(
                PriorityCalculator.Calculate(donation, asOfUtc ?? DateTime.UtcNow, urgencyTimeConstantMinutes) / 100.0,
                0, 1);

            // שימוש ב-WeightKg בהתאם למדד קיבולת הרכב בפועל בקילוגרמים
            double capacityRatio = volunteer.MaxCapacityKg <= 0
                ? 0
                : Math.Clamp(volunteer.MaxCapacityKg / Math.Max(donation.TotalWeightKg(), 0.001), 0, 2) / 2.0;

            // נורמליזציה נכונה של ציון האמינות לפי סקאלה של 1 עד 5
            double normalizedReliability = Math.Clamp(volunteer.ReliabilityScore / 5.0, 0, 1);

            return (normalizedDistance * DistanceWeight)
                 + (normalizedUrgency * UrgencyWeight)
                 + (capacityRatio * CapacityWeight)
                 + (normalizedReliability * ReliabilityWeight);
        }
    }

    /// <summary>
    /// מימוש עצמאי (לא שימוש ב-List.Sort המובנה) של Merge Sort ו-Quick Sort,
    /// גנרי - כדי שיהיה ניתן להשתמש בו לכל T, לא רק למתנדבים.
    /// אומת ידנית: שני האלגוריתמים נכונים כפי שהם (Median-of-three ל-QuickSort,
    /// Merge יציב), לא נמצא באג לוגי - רק הרחבה קלה בהמשך הקובץ.
    /// </summary>
    public static class CustomSortAlgorithms<T>
    {
        public static List<T> MergeSort(List<T> items, Comparison<T> comparer)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count <= 1) return new List<T>(items);

            int mid = items.Count / 2;
            var left = MergeSort(items.GetRange(0, mid), comparer);
            var right = MergeSort(items.GetRange(mid, items.Count - mid), comparer);

            return Merge(left, right, comparer);
        }

        private static List<T> Merge(List<T> left, List<T> right, Comparison<T> comparer)
        {
            var result = new List<T>(left.Count + right.Count);
            int i = 0, j = 0;

            while (i < left.Count && j < right.Count)
            {
                if (comparer(left[i], right[j]) <= 0)
                    result.Add(left[i++]);
                else
                    result.Add(right[j++]);
            }

            while (i < left.Count) result.Add(left[i++]);
            while (j < right.Count) result.Add(right[j++]);

            return result;
        }

        public static void QuickSort(List<T> items, Comparison<T> comparer)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            QuickSortRange(items, 0, items.Count - 1, comparer);
        }

        private static void QuickSortRange(List<T> items, int low, int high, Comparison<T> comparer)
        {
            while (low < high)
            {
                int pivotIndex = Partition(items, low, high, comparer);

                if (pivotIndex - low < high - pivotIndex)
                {
                    QuickSortRange(items, low, pivotIndex - 1, comparer);
                    low = pivotIndex + 1;
                }
                else
                {
                    QuickSortRange(items, pivotIndex + 1, high, comparer);
                    high = pivotIndex - 1;
                }
            }
        }

        private static int Partition(List<T> items, int low, int high, Comparison<T> comparer)
        {
            int mid = low + (high - low) / 2;
            MedianOfThree(items, low, mid, high, comparer);

            Swap(items, mid, high - 1);
            T pivot = items[high - 1];

            int i = low;
            for (int j = low; j < high - 1; j++)
            {
                if (comparer(items[j], pivot) <= 0)
                {
                    Swap(items, i, j);
                    i++;
                }
            }

            Swap(items, i, high - 1);
            return i;
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
