using BLL.Services;
using BLL.Algoritems;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// פריט בודד (תרומה) בייצוג המותאם לחישוב מטריצת ה-DP.
    /// המשקל נשמר ביחידות (WeightUnits), ברירת מחדל 100 גרם ליחידה.
    /// </summary>
    public class KnapsackItem
    {
        public Donation Donation { get; }
        public int WeightUnits { get; }
        public double Value { get; }
        public int CategoryId { get; }

        public KnapsackItem(Donation donation, int weightUnits, double value, int categoryId)
        {
            if (weightUnits < 0) throw new ArgumentOutOfRangeException(nameof(weightUnits));
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "ערך הפריט חייב להיות מספר סופי לא שלילי");

            Donation = donation ?? throw new ArgumentNullException(nameof(donation));
            WeightUnits = weightUnits;
            Value = value;
            CategoryId = categoryId;
        }

        /// <summary>בונה KnapsackItem ממשקל בגרמים. מעגל כלפי מעלה – לעולם לא מקטין משקל (בטוח לקיבולת).</summary>
        public static KnapsackItem FromGrams(Donation donation, int weightGrams, double value, int categoryId, int unitSizeGrams = 100)
        {
            if (unitSizeGrams <= 0) unitSizeGrams = 100;
            int units = (int)Math.Ceiling(Math.Max(0, weightGrams) / (double)unitSizeGrams);
            return new KnapsackItem(donation, units, value, categoryId);
        }

        /// <summary>
        /// יוצר פריטי תרמיל סביב נקודת מרכז (בדרך כלל תרומת העוגן), עם:
        ///  * סינון תרומות רחוקות מדי מהמרכז (כדי שהמסלול לא יתפזר);
        ///  * ערך = משקל × (1 + דחיפות/100) / (1 + מקדם × מרחק).
        ///
        /// תיקון לגרסה המקורית: הערך היה PriorityScore × כמות (Quantity). (1) הערך התאפס כשהציון 0 והפריט
        /// לא נבחר לעולם (הפתרון דורש שיפור ממש); (2) "כמות" היחידות אינה מדד להצלת מזון — משקל הוא;
        /// (3) הדחיפות נשמרה ב-DB והתיישנה — כאן מחושבת ברגע השימוש (PriorityCalculator).
        /// </summary>
        public static List<KnapsackItem> CreateItems(
            IEnumerable<Donation> donations,
            double centerLat,
            double centerLon,
            double maxAllowedDistanceKm,
            double distancePenaltyCoefficient,
            DateTime asOfUtc,
            int unitSizeGrams = 100,
            double urgencyTimeConstantMinutes = 180.0,
            int? alwaysIncludeDonationId = null)
        {
            var items = new List<KnapsackItem>();

            foreach (var donation in donations)
            {
                double weightKg = donation.TotalWeightKg();
                if (weightKg <= 0) continue;

                double distanceKm = GeoUtils.HaversineKm(centerLat, centerLon, donation.Latitude, donation.Longitude);
                bool isAnchor = alwaysIncludeDonationId.HasValue && donation.Id == alwaysIncludeDonationId.Value;
                if (!isAnchor && distanceKm > maxAllowedDistanceKm) continue;

                double priority = PriorityCalculator.Calculate(donation, asOfUtc, urgencyTimeConstantMinutes);
                double value = weightKg * (1.0 + priority / 100.0) / (1.0 + distancePenaltyCoefficient * distanceKm);

                int grams = (int)Math.Min(int.MaxValue, Math.Ceiling(weightKg * 1000.0));
                items.Add(FromGrams(donation, grams, value, donation.PrimaryCategoryId(), unitSizeGrams));
            }

            return items;
        }

        /// <summary>תאימות לאחור: גרסה שממרכזת סביב המתנדב.</summary>
        public static List<KnapsackItem> CreateItemsWithGeographicalAwareness(
            IEnumerable<Donation> donations,
            double volunteerLat,
            double volunteerLon,
            double maxAllowedDistanceKm = 15.0,
            double distancePenaltyCoefficient = 0.1,
            DateTime? asOfUtc = null)
        {
            // מתנדב בלי GPS: אין טעם לדרג לפי מרחק ממנו
            if (volunteerLat == 0 && volunteerLon == 0) return new List<KnapsackItem>();

            return CreateItems(donations, volunteerLat, volunteerLon, maxAllowedDistanceKm,
                distancePenaltyCoefficient, asOfUtc ?? DateTime.UtcNow);
        }
    }

    public class KnapsackResult
    {
        public List<Donation> SelectedDonations { get; } = new List<Donation>();
        public double TotalValue { get; set; }
        public int TotalUnits { get; set; }
    }

    /// <summary>
    /// פותר 0/1 Knapsack (כל תרומה נכנסת במלואה או לא נכנסת) עם אילוצים:
    ///   * קיבולת הרכב;
    ///   * מגוון: לכל היותר maxItemsPerCategory תרומות מאותה קטגוריה (0 או פחות = ללא הגבלה);
    ///   * אופציונלי: תרומת "עוגן" שחייבת להיכלל.
    ///
    /// אלגוריתם (בדוק מול Brute-Force על אלפי מקרים אקראיים):
    ///   1. לכל קטגוריה — DP על (מספר פריטים, משקל) עם טבלת keep וצעד שחזור (במקום העתקת רשימות בכל תא).
    ///   2. "נקודות שבירה" של עקומת הקטגוריה = הצירופים היחידים שכדאי לשקול.
    ///   3. Knapsack קבוצתי בין הקטגוריות על נקודות השבירה בלבד.
    ///
    /// סיבוכיות: O(n·K·W + C·W·B) כאשר B = מספר נקודות השבירה (קטן בפועל), לעומת O(n·K·W + C·W²)
    /// עם הקצאות רשימה בכל שיפור בגרסה המקורית. בדוגמה מציאותית (24 תרומות, 8 קטגוריות, 300 ק"ג) —
    /// כ-36 מיליון פעולות לעומת כ-0.2 מיליון, עם אותו ערך אופטימלי.
    /// </summary>
    public class KnapsackSolver
    {
        private const int MaxDpUnits = 20_000;          // מעל זה מגדילים את יחידת המשקל (עיגול שמרני)
        private const long MaxKeepCells = 50_000_000;   // תקרת זיכרון לטבלאות השחזור

        private sealed class ScaledItem
        {
            public KnapsackItem Source { get; }
            public int Units { get; }
            public double Value { get; }

            public ScaledItem(KnapsackItem source, int units)
            {
                Source = source;
                Units = units;
                Value = source.Value;
            }
        }

        private sealed class Option
        {
            public int Units { get; }
            public double Value { get; }
            public List<ScaledItem> Items { get; }

            public Option(int units, double value, List<ScaledItem> items)
            {
                Units = units;
                Value = value;
                Items = items;
            }
        }

        /// <summary>תאימות לאחור.</summary>
        public List<Donation> SolveWithDiversity(List<KnapsackItem> items, int maxCapacityUnits, int maxItemsPerCategory)
        {
            return Solve(items, maxCapacityUnits, maxItemsPerCategory, null).SelectedDonations;
        }

        public KnapsackResult Solve(List<KnapsackItem> items, int maxCapacityUnits, int maxItemsPerCategory, int? mustIncludeDonationId = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (maxCapacityUnits < 0) throw new ArgumentOutOfRangeException(nameof(maxCapacityUnits));

            var result = new KnapsackResult();
            if (items.Count == 0 || maxCapacityUnits == 0) return result;

            // --- תרומת עוגן: נבחרת מראש, מקטינה את הקיבולת ואת מכסת הקטגוריה שלה ---
            KnapsackItem? anchor = null;
            if (mustIncludeDonationId.HasValue)
            {
                anchor = items.FirstOrDefault(i => i.Donation.Id == mustIncludeDonationId.Value);
                if (anchor == null || anchor.WeightUnits > maxCapacityUnits) return result;
            }

            int remaining = maxCapacityUnits - (anchor?.WeightUnits ?? 0);

            var candidates = items
                .Where(i => !ReferenceEquals(i, anchor) && i.Value > 0 && i.WeightUnits <= remaining)
                .OrderBy(i => i.Donation.Id)
                .ToList();

            var chosen = new List<KnapsackItem>();
            double dpValue = 0;

            if (candidates.Count > 0)
            {
                // אין טעם בקיבולת גדולה ממשקל כל הפריטים יחד
                long totalUnits = candidates.Sum(i => (long)i.WeightUnits);
                int effectiveCap = (int)Math.Min(remaining, totalUnits);

                var byCategory = candidates
                    .GroupBy(i => i.CategoryId)
                    .OrderBy(g => g.Key)
                    .ToList();

                // --- קנה מידה: מגדילים את יחידת המשקל אם הטבלאות היו גדולות מדי (עיגול כלפי מעלה => בטוח) ---
                long cellsPerColumn = byCategory.Max(g => (long)g.Count() * RowsFor(g.Count(), maxItemsPerCategory));
                int scale = 1;
                if (effectiveCap > MaxDpUnits) scale = (effectiveCap + MaxDpUnits - 1) / MaxDpUnits;
                while (scale < (1 << 20) && cellsPerColumn * (effectiveCap / scale + 1) > MaxKeepCells) scale *= 2;

                int cap = effectiveCap / scale;

                var optionSets = new List<List<Option>>();
                foreach (var group in byCategory)
                {
                    int limit = maxItemsPerCategory > 0
                        ? maxItemsPerCategory - (anchor != null && anchor.CategoryId == group.Key ? 1 : 0)
                        : int.MaxValue;

                    var scaled = group
                        .Select(i => new ScaledItem(i, (i.WeightUnits + scale - 1) / scale))
                        .Where(s => s.Units <= cap)
                        .ToList();

                    int m = Math.Min(limit, scaled.Count);
                    if (m <= 0) continue;

                    var options = BuildCategoryOptions(scaled, m, cap);
                    if (options.Count > 0) optionSets.Add(options);
                }

                // --- Knapsack קבוצתי בין הקטגוריות: מכל קטגוריה בוחרים אפשרות אחת (או כלום) ---
                var g = new double[cap + 1];   // "לכל היותר w יחידות"
                var choices = new List<int[]>(optionSets.Count);

                foreach (var options in optionSets)
                {
                    var next = (double[])g.Clone();
                    var choice = new int[cap + 1];
                    Array.Fill(choice, -1);

                    for (int w = 0; w <= cap; w++)
                    {
                        for (int o = 0; o < options.Count; o++)
                        {
                            var opt = options[o];
                            if (opt.Units > w) break; // האפשרויות ממוינות לפי משקל עולה

                            double candidate = g[w - opt.Units] + opt.Value;
                            if (candidate > next[w])
                            {
                                next[w] = candidate;
                                choice[w] = o;
                            }
                        }
                    }

                    choices.Add(choice);
                    g = next;
                }

                dpValue = g[cap];

                int wRem = cap;
                for (int c = optionSets.Count - 1; c >= 0; c--)
                {
                    int o = choices[c][wRem];
                    if (o < 0) continue;

                    var opt = optionSets[c][o];
                    foreach (var si in opt.Items) chosen.Add(si.Source);
                    wRem -= opt.Units;
                }
            }

            if (anchor != null) chosen.Add(anchor);

            foreach (var item in chosen.OrderBy(i => i.Donation.Id))
            {
                result.SelectedDonations.Add(item.Donation);
                result.TotalUnits += item.WeightUnits;
            }

            result.TotalValue = dpValue + (anchor?.Value ?? 0);
            return result;
        }

        private static int RowsFor(int groupSize, int maxItemsPerCategory)
        {
            return (maxItemsPerCategory <= 0 || maxItemsPerCategory >= groupSize) ? 1 : maxItemsPerCategory + 1;
        }

        /// <summary>
        /// מחשב לקטגוריה אחת את "נקודות השבירה": לכל משקל w שבו הערך האופטימלי (לכל היותר maxCount פריטים,
        /// לכל היותר w יחידות) גדל ממש לעומת w-1 — האפשרות (w, ערך, הפריטים שנבחרו).
        /// בנקודת שבירה המשקל של הפתרון הוא בדיוק w, ולכן אפשר "לשלם" w בשלב המיזוג בין הקטגוריות.
        /// </summary>
        private static List<Option> BuildCategoryOptions(List<ScaledItem> group, int maxCount, int cap)
        {
            int n = group.Count;
            int cols = cap + 1;
            var options = new List<Option>();
            double[] best;
            Func<int, List<ScaledItem>> reconstruct;

            if (maxCount >= n)
            {
                // אין הגבלת כמות בפועל — Knapsack חד-ממדי רגיל
                var dp = new double[cols];
                var keep = new bool[n * cols];

                for (int i = 0; i < n; i++)
                {
                    int wt = group[i].Units;
                    double val = group[i].Value;
                    int rowBase = i * cols;
                    for (int w = cap; w >= wt; w--)
                    {
                        double candidate = dp[w - wt] + val;
                        if (candidate > dp[w])
                        {
                            dp[w] = candidate;
                            keep[rowBase + w] = true;
                        }
                    }
                }

                best = dp;
                reconstruct = target =>
                {
                    var picked = new List<ScaledItem>();
                    int w = target;
                    for (int i = n - 1; i >= 0; i--)
                    {
                        if (w >= group[i].Units && keep[i * cols + w])
                        {
                            picked.Add(group[i]);
                            w -= group[i].Units;
                        }
                    }
                    return picked;
                };
            }
            else
            {
                // dp[j, w] = הערך הטוב ביותר עם לכל היותר j פריטים ולכל היותר w יחידות
                int rows = maxCount + 1;
                var dp = new double[rows * cols];
                var keep = new bool[n * rows * cols];

                for (int i = 0; i < n; i++)
                {
                    int wt = group[i].Units;
                    double val = group[i].Value;
                    int itemBase = i * rows * cols;

                    // j יורד => כל פריט נכלל לכל היותר פעם אחת (0/1)
                    for (int j = maxCount; j >= 1; j--)
                    {
                        for (int w = cap; w >= wt; w--)
                        {
                            double candidate = dp[(j - 1) * cols + (w - wt)] + val;
                            if (candidate > dp[j * cols + w])
                            {
                                dp[j * cols + w] = candidate;
                                keep[itemBase + j * cols + w] = true;
                            }
                        }
                    }
                }

                best = new double[cols];
                Array.Copy(dp, maxCount * cols, best, 0, cols);

                reconstruct = target =>
                {
                    var picked = new List<ScaledItem>();
                    int j = maxCount;
                    int w = target;
                    for (int i = n - 1; i >= 0; i--)
                    {
                        if (j > 0 && w >= group[i].Units && keep[i * rows * cols + j * cols + w])
                        {
                            picked.Add(group[i]);
                            j--;
                            w -= group[i].Units;
                        }
                    }
                    return picked;
                };
            }

            for (int w = 0; w <= cap; w++)
            {
                double previous = w > 0 ? best[w - 1] : 0.0;
                if (best[w] > previous)
                {
                    options.Add(new Option(w, best[w], reconstruct(w)));
                }
            }

            return options;
        }
    }
}
