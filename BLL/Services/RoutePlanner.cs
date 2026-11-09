using System;
using System.Collections.Generic;
using System.Numerics;

namespace BLL.Algorithms
{
    /// <summary>תחנת איסוף מועמדת לתכנון מסלול (תרומה אחת).</summary>
    public sealed class PlannerStop
    {
        public int DonationId { get; init; }
        public DateTime PickupDeadlineUtc { get; init; }
        public DateTime ExpirationUtc { get; init; }
        public double Value { get; init; }
        public double WeightKg { get; init; }
    }

    /// <summary>נתוני התכנון של מסלול אחד: מתנדב אחד, עמותה אחת, משבצת זמינות אחת.</summary>
    public sealed class PlannerInput
    {
        /// <summary>התחנות האפשריות. Stops[AnchorIndex] (תרומת העוגן) חייבת להיות במסלול.</summary>
        public IReadOnlyList<PlannerStop> Stops { get; init; } = Array.Empty<PlannerStop>();
        public int AnchorIndex { get; init; }

        /// <summary>זמני נסיעה (דקות) ומרחקים (ק"מ): אינדקס 0 = נקודת היציאה, 1..m = התחנות, m+1 = העמותה.</summary>
        public double[,] Minutes { get; init; } = new double[0, 0];
        public double[,] Km { get; init; } = new double[0, 0];

        public DateTime EarliestDepartureUtc { get; init; }
        public DateTime LatestDepartureUtc { get; init; }

        /// <summary>סוף משבצת הזמינות של המתנדב - ההגעה לעמותה לא אחריו.</summary>
        public DateTime AvailableUntilUtc { get; init; } = DateTime.MaxValue;

        public double ServiceMinutes { get; init; }
        public double CapacityKg { get; init; } = double.MaxValue;

        /// <summary>הרגע המוקדם ביותר בטווח [from, to] שבו העמותה פתוחה (null = סגורה בכל הטווח).</summary>
        public Func<DateTime, DateTime, DateTime?> EarliestNgoOpen { get; init; } = (from, _) => from;

        /// <summary>true = אסור שהמסלול יסתיים ברגע הזה (למשל הוא נכנס לשבת).</summary>
        public Func<DateTime, bool>? IsArrivalBlocked { get; init; }
    }

    /// <summary>המסלול שנבחר: סדר התחנות, שעת היציאה וזמני ההגעה.</summary>
    public sealed class PlannerResult
    {
        public IReadOnlyList<int> Order { get; init; } = Array.Empty<int>();          // אינדקסים ל-Stops לפי סדר הנסיעה
        public IReadOnlyList<DateTime> ArrivalsUtc { get; init; } = Array.Empty<DateTime>();
        public DateTime DepartureUtc { get; init; }
        public DateTime NgoArrivalUtc { get; init; }
        public double Value { get; init; }
        public double WeightKg { get; init; }
        public double DrivingMinutes { get; init; }
        public double DrivingKm { get; init; }
    }

    /// <summary>
    /// תיקונים 22, 23, 26 - תכנון מסלול מדויק: איזה תחנות, באיזה סדר, ומתי לצאת.
    ///
    /// בעבר: סדר התחנות נקבע ב"שכן הקרוב" (חמדני, בלי הקטע האחרון לעמותה), תחנות שלא הספיקו "נשמטו" בשקט,
    /// ואם המסלול המלא חרג מזמינות המתנדב / משעות העמותה - כל המסלול נפסל, גם כשתת-קבוצה שלו הייתה תקינה.
    ///
    /// עכשיו: תכנון דינמי (Held-Karp) על כל תתי-הקבוצות של התחנות שכוללות את תרומת העוגן:
    ///   arrive[S, j] = זמן ההגעה המוקדם ביותר לתחנה j, אחרי שביקרנו בדיוק בתחנות S, בלי לאחר לאף מועד איסוף.
    ///   arrive[S ∪ {k}, k] = min over j ∈ S of ( arrive[S, j] + זמן שירות + נסיעה(j → k) ), רק אם ≤ מועד האיסוף של k.
    /// לכל קבוצה S נבדקים כל אילוצי המסלול: הגעה לעמותה בשעות הקבלה, לפני התפוגה של כל התרומות, לפני סוף
    /// זמינות המתנדב, לא בשבת, וקיבולת הרכב. נבחרת הקבוצה עם הערך הגבוה ביותר (ובשוויון - ההגעה המוקדמת).
    ///
    /// למה זה מדויק: לתחנות יש רק מועד אחרון (אין "לא לפני"), ולכן הגעה מוקדמת יותר לאותו מצב (S, j) תמיד
    /// טובה לפחות כמו מאוחרת - מספיק לשמור את המוקדמת (עיקרון האופטימליות של DP).
    ///
    /// עמותה שעדיין סגורה: אם אפשר לדחות את היציאה (בגבולות מועדי האיסוף, אופק התכנון וזמינות המתנדב)
    /// כך שההגעה תהיה בשעות הקבלה - היציאה נדחית במקום לפסול את המסלול.
    ///
    /// סיבוכיות: O(2^m · m²) זמן ו-O(2^m · m) זיכרון, m ≤ MaxExactStops (12 תחנות = פחות ממיליון צעדים).
    /// </summary>
    public static class RoutePlanner
    {
        public const int MaxExactStops = 12;
        private const double Eps = 1e-9;
        private const double MaxDelayMinutes = 24 * 60;   // הגנה טכנית בלבד (אופק התכנון קצר בהרבה)

        public static PlannerResult? Plan(PlannerInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            int m = input.Stops.Count;
            if (m == 0) return null;
            if (m > MaxExactStops)
                throw new ArgumentException($"לכל היותר {MaxExactStops} תחנות לתכנון מדויק.", nameof(input));
            if (input.AnchorIndex < 0 || input.AnchorIndex >= m)
                throw new ArgumentOutOfRangeException(nameof(input), "תרומת העוגן חייבת להיות אחת התחנות.");
            if (input.Minutes.GetLength(0) != m + 2 || input.Minutes.GetLength(1) != m + 2)
                throw new ArgumentException("מטריצת הזמנים חייבת להיות בגודל (m+2)x(m+2).", nameof(input));

            var t0 = input.EarliestDepartureUtc;
            if (input.LatestDepartureUtc < t0) return null;

            int ngo = m + 1;
            int full = 1 << m;
            double service = Math.Max(0, input.ServiceMinutes);

            // מועדי האיסוף והתפוגה - בדקות מרגע היציאה המוקדם ביותר
            var deadline = new double[m];
            var expiration = new double[m];
            for (int i = 0; i < m; i++)
            {
                deadline[i] = (input.Stops[i].PickupDeadlineUtc - t0).TotalMinutes;
                expiration[i] = (input.Stops[i].ExpirationUtc - t0).TotalMinutes;
            }

            // ---------------- DP על תתי-קבוצות (Held-Karp) ----------------
            var arrive = new double[full * m];
            var parent = new sbyte[full * m];
            Array.Fill(arrive, double.PositiveInfinity);

            for (int i = 0; i < m; i++)
            {
                double a = input.Minutes[0, i + 1];
                if (a <= deadline[i] + Eps)
                {
                    arrive[(1 << i) * m + i] = a;
                    parent[(1 << i) * m + i] = -1;
                }
            }

            // כל קבוצה מעובדת אחרי כל תתי-הקבוצות שלה (סדר מספרי עולה), ולכן הערכים שלה כבר סופיים
            for (int mask = 1; mask < full; mask++)
            {
                for (int j = 0; j < m; j++)
                {
                    if ((mask & (1 << j)) == 0) continue;
                    double current = arrive[mask * m + j];
                    if (double.IsPositiveInfinity(current)) continue;

                    double leave = current + service;
                    for (int k = 0; k < m; k++)
                    {
                        if ((mask & (1 << k)) != 0) continue;
                        double a = leave + input.Minutes[j + 1, k + 1];
                        if (a > deadline[k] + Eps) continue;   // מאחרים למועד האיסוף של k

                        int index = (mask | (1 << k)) * m + k;
                        if (a < arrive[index] - Eps)
                        {
                            arrive[index] = a;
                            parent[index] = (sbyte)j;
                        }
                    }
                }
            }

            // ערך / משקל / התפוגה הקרובה של כל קבוצה - בחישוב מצטבר לפי הביט הנמוך
            var value = new double[full];
            var weight = new double[full];
            var minExpiration = new double[full];
            minExpiration[0] = double.PositiveInfinity;
            for (int mask = 1; mask < full; mask++)
            {
                int i = BitOperations.TrailingZeroCount(mask);
                int rest = mask & (mask - 1);
                value[mask] = value[rest] + input.Stops[i].Value;
                weight[mask] = weight[rest] + input.Stops[i].WeightKg;
                minExpiration[mask] = Math.Min(minExpiration[rest], expiration[i]);
            }

            // ---------------- בחירת הקבוצה הטובה ביותר שעומדת בכל אילוצי המסלול ----------------
            int anchorBit = 1 << input.AnchorIndex;
            double maxDelayByDeparture = (input.LatestDepartureUtc - t0).TotalMinutes;
            double availableMinutes = input.AvailableUntilUtc == DateTime.MaxValue
                ? double.PositiveInfinity
                : (input.AvailableUntilUtc - t0).TotalMinutes;

            int bestMask = 0, bestEnd = -1;
            double bestValue = double.NegativeInfinity, bestArrival = double.PositiveInfinity, bestDelay = 0;

            for (int mask = 1; mask < full; mask++)
            {
                if ((mask & anchorBit) == 0) continue;
                if (weight[mask] > input.CapacityKg + Eps) continue;
                if (value[mask] < bestValue - Eps) continue;   // לא יכולה לנצח

                // התחנה האחרונה שממנה ההגעה לעמותה הכי מוקדמת
                double arrival = double.PositiveInfinity;
                int end = -1;
                for (int j = 0; j < m; j++)
                {
                    if ((mask & (1 << j)) == 0) continue;
                    double current = arrive[mask * m + j];
                    if (double.IsPositiveInfinity(current)) continue;
                    double a = current + service + input.Minutes[j + 1, ngo];
                    if (a < arrival - Eps) { arrival = a; end = j; }
                }
                if (end < 0) continue;

                // אילוצים שדחיית היציאה רק מחמירה: תפוגה וזמינות המתנדב
                if (arrival > minExpiration[mask] + Eps || arrival > availableMinutes + Eps) continue;

                // כמה אפשר לדחות את היציאה: מרווח מועדי האיסוף במסלול, אופק התכנון, זמינות, תפוגה
                double maxDelay = Math.Min(
                    Math.Min(PathSlack(arrive, parent, deadline, m, mask, end), maxDelayByDeparture),
                    Math.Min(availableMinutes - arrival, minExpiration[mask] - arrival));
                if (maxDelay < 0) maxDelay = 0;
                maxDelay = Math.Min(maxDelay, MaxDelayMinutes);

                var arrivalUtc = t0.AddMinutes(arrival);
                var open = input.EarliestNgoOpen(arrivalUtc, arrivalUtc.AddMinutes(maxDelay));
                if (!open.HasValue) continue;   // העמותה סגורה בכל הטווח האפשרי

                double delay = Math.Max(0, (open.Value - arrivalUtc).TotalMinutes);
                double finalArrival = arrival + delay;
                if (input.IsArrivalBlocked?.Invoke(t0.AddMinutes(finalArrival)) == true) continue;

                bool better = value[mask] > bestValue + Eps ||
                              (Math.Abs(value[mask] - bestValue) <= Eps && finalArrival < bestArrival - Eps);
                if (!better) continue;

                bestMask = mask;
                bestEnd = end;
                bestValue = value[mask];
                bestArrival = finalArrival;
                bestDelay = delay;
            }

            if (bestEnd < 0) return null;

            // ---------------- שחזור הסדר מהמצביעים ----------------
            var order = new List<int>();
            for (int state = bestMask, j = bestEnd; j >= 0;)
            {
                order.Add(j);
                int previous = parent[state * m + j];
                state ^= 1 << j;
                j = previous;
            }
            order.Reverse();

            var departure = t0.AddMinutes(bestDelay);
            var arrivals = new List<DateTime>(order.Count);
            double drivingMinutes = 0, drivingKm = 0;
            int from = 0, visited = 0;
            foreach (int stop in order)
            {
                visited |= 1 << stop;
                arrivals.Add(departure.AddMinutes(arrive[visited * m + stop]));
                drivingMinutes += input.Minutes[from, stop + 1];
                drivingKm += input.Km[from, stop + 1];
                from = stop + 1;
            }
            drivingMinutes += input.Minutes[from, ngo];
            drivingKm += input.Km[from, ngo];

            return new PlannerResult
            {
                Order = order,
                ArrivalsUtc = arrivals,
                DepartureUtc = departure,
                NgoArrivalUtc = t0.AddMinutes(bestArrival),
                Value = bestValue,
                WeightKg = weight[bestMask],
                DrivingMinutes = drivingMinutes,
                DrivingKm = drivingKm
            };
        }

        // המרווח הקטן ביותר בין מועד איסוף לזמן ההגעה לאורך המסלול - כמה אפשר לדחות את כולו בלי לאחר
        private static double PathSlack(double[] arrive, sbyte[] parent, double[] deadline, int m, int mask, int end)
        {
            double slack = double.PositiveInfinity;
            for (int state = mask, j = end; j >= 0;)
            {
                slack = Math.Min(slack, deadline[j] - arrive[state * m + j]);
                int previous = parent[state * m + j];
                state ^= 1 << j;
                j = previous;
            }
            return slack;
        }
    }
}
