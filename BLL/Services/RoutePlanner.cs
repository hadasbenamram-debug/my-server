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

        /// <summary>תיקון 43: "זמינה לאיסוף החל מ-" (null = אפשר לאסוף מיד). מתנדב שמגיע מוקדם יותר - מחכה.</summary>
        public DateTime? EarliestPickupUtc { get; init; }

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

        /// <summary>
        /// תיקון 43: כמה דקות (בסך הכול) מותר למתנדב לחכות בתחנות לתרומות שעוד לא מוכנות ("זמינה החל מ-").
        /// אינסוף = ללא הגבלה. בדרך כלל אין המתנה בכלל - היציאה נדחית כך שהמתנדב מגיע כשהתרומה מוכנה.
        /// </summary>
        public double MaxWaitMinutes { get; init; } = double.PositiveInfinity;

        /// <summary>הרגע המוקדם ביותר בטווח [from, to] שבו העמותה פתוחה (null = סגורה בכל הטווח).</summary>
        public Func<DateTime, DateTime, DateTime?> EarliestNgoOpen { get; init; } = (from, _) => from;

        /// <summary>true = אסור שהמסלול יסתיים ברגע הזה (למשל הוא נכנס לשבת).</summary>
        public Func<DateTime, bool>? IsArrivalBlocked { get; init; }
    }

    /// <summary>המסלול שנבחר: סדר התחנות, שעת היציאה וזמני ההגעה.</summary>
    public sealed class PlannerResult
    {
        public IReadOnlyList<int> Order { get; init; } = Array.Empty<int>();          // אינדקסים ל-Stops לפי סדר הנסיעה

        /// <summary>מתי מתחיל האיסוף בכל תחנה (תיקון 43: מתנדב שהגיע לפני "זמינה החל מ-" - אחרי ההמתנה).</summary>
        public IReadOnlyList<DateTime> ArrivalsUtc { get; init; } = Array.Empty<DateTime>();
        public DateTime DepartureUtc { get; init; }
        public DateTime NgoArrivalUtc { get; init; }
        public double Value { get; init; }
        public double WeightKg { get; init; }
        public double DrivingMinutes { get; init; }
        public double DrivingKm { get; init; }

        /// <summary>תיקון 43: סך ההמתנה בתחנות (דקות) לתרומות שעוד לא היו מוכנות.</summary>
        public double WaitMinutes { get; init; }
    }

    /// <summary>
    /// תיקונים 22, 23, 26, 43 - תכנון מסלול מדויק: איזה תחנות, באיזה סדר, ומתי לצאת.
    ///
    /// בעבר: סדר התחנות נקבע ב"שכן הקרוב" (חמדני, בלי הקטע האחרון לעמותה), תחנות שלא הספיקו "נשמטו" בשקט,
    /// ואם המסלול המלא חרג מזמינות המתנדב / משעות העמותה - כל המסלול נפסל, גם כשתת-קבוצה שלו הייתה תקינה.
    ///
    /// עכשיו: תכנון דינמי (Held-Karp) על כל תתי-הקבוצות של התחנות שכוללות את תרומת העוגן:
    ///   arrive[S, j] = הרגע המוקדם ביותר שבו אפשר להתחיל לאסוף בתחנה j, אחרי שביקרנו בדיוק בתחנות S, בלי לאחר לאף מועד איסוף.
    ///   arrive[S ∪ {k}, k] = min over j ∈ S of max( arrive[S, j] + זמן שירות + נסיעה(j → k), "זמינה החל מ-" של k ),
    ///   רק אם ≤ מועד האיסוף של k.
    /// לכל קבוצה S נבדקים כל אילוצי המסלול: הגעה לעמותה בשעות הקבלה, לפני התפוגה של כל התרומות, לפני סוף
    /// זמינות המתנדב, לא בשבת, וקיבולת הרכב. נבחרת הקבוצה עם הערך הגבוה ביותר (ובשוויון - ההגעה המוקדמת).
    ///
    /// למה זה מדויק: מותר להגיע לתחנה לפני שהתרומה מוכנה ולחכות (תיקון 43), ולכן הגעה מוקדמת יותר לאותו מצב (S, j)
    /// תמיד טובה לפחות כמו מאוחרת - max ו-+ שומרים על הסדר - ומספיק לשמור את המוקדמת (עיקרון האופטימליות של DP).
    ///
    /// מתי לצאת (TryTiming): לכל מסלול מחושב בנוסחה סגורה טווח שעות היציאה שעומד באילוצים - מועדי האיסוף,
    /// התפוגה, סוף המשמרת, אופק התכנון והמתנה של לכל היותר MaxWaitMinutes - ונבחרת היציאה הראשונה שבה העמותה
    /// פתוחה בהגעה (עמותה שעדיין סגורה: היציאה נדחית במקום לפסול את המסלול). אם המתנדב עדיין היה מחכה לתרומה
    /// שלא מוכנה, היציאה נדחית עוד, עד שאין המתנה - ההגעה לעמותה לא משתנה, והמתנדב לא מחכה סתם.
    /// מגבלת ההמתנה נבדקת על המסלול המהיר ביותר של כל קבוצה (לכל תחנה אחרונה). היא נועדה לפסול צירופים שבהם
    /// המתנדב היה מחכה הרבה (תחנה שצריך לאסוף מוקדם + תחנה שמוכנה רק מאוחר) - במקרה כזה נבחרת קבוצה אחרת.
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
            double maxWait = double.IsNaN(input.MaxWaitMinutes) ? double.PositiveInfinity : Math.Max(0, input.MaxWaitMinutes);

            // מועדי האיסוף, "זמינה החל מ-" (תיקון 43) והתפוגה - בדקות מרגע היציאה המוקדם ביותר
            var deadline = new double[m];
            var release = new double[m];
            var expiration = new double[m];
            for (int i = 0; i < m; i++)
            {
                var stop = input.Stops[i];
                deadline[i] = (stop.PickupDeadlineUtc - t0).TotalMinutes;
                release[i] = stop.EarliestPickupUtc.HasValue
                    ? (stop.EarliestPickupUtc.Value - t0).TotalMinutes
                    : double.NegativeInfinity;   // מוכנה מיד
                expiration[i] = (stop.ExpirationUtc - t0).TotalMinutes;
            }

            // ---------------- DP על תתי-קבוצות (Held-Karp) ----------------
            // arrive = תחילת האיסוף (אחרי המתנה, אם הגענו לפני שהתרומה מוכנה)
            var arrive = new double[full * m];
            var parent = new sbyte[full * m];
            Array.Fill(arrive, double.PositiveInfinity);

            for (int i = 0; i < m; i++)
            {
                double a = Math.Max(input.Minutes[0, i + 1], release[i]);
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
                        double a = Math.Max(leave + input.Minutes[j + 1, k + 1], release[k]);   // מוקדם מדי - מחכים
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

            var path = new int[m];
            var endOrder = new int[m];
            var endArrival = new double[m];

            int bestMask = 0, bestEnd = -1;
            double bestValue = double.NegativeInfinity, bestArrival = double.PositiveInfinity, bestDelay = 0;

            for (int mask = 1; mask < full; mask++)
            {
                if ((mask & anchorBit) == 0) continue;
                if (weight[mask] > input.CapacityKg + Eps) continue;
                if (value[mask] < bestValue - Eps) continue;   // לא יכולה לנצח

                // התחנות האחרונות האפשריות, לפי ההגעה לעמותה בלי לדחות את היציאה (המוקדמת ראשונה; בשוויון - האינדקס הנמוך)
                int ends = 0;
                for (int j = 0; j < m; j++)
                {
                    if ((mask & (1 << j)) == 0) continue;
                    double current = arrive[mask * m + j];
                    if (double.IsPositiveInfinity(current)) continue;

                    double a = current + service + input.Minutes[j + 1, ngo];
                    int pos = ends++;
                    while (pos > 0 && endArrival[pos - 1] > a + Eps)
                    {
                        endArrival[pos] = endArrival[pos - 1];
                        endOrder[pos] = endOrder[pos - 1];
                        pos--;
                    }
                    endArrival[pos] = a;
                    endOrder[pos] = j;
                }

                // התחנה האחרונה הראשונה (לפי הסדר הזה) שעומדת בכל האילוצים היא גם זו שמגיעה הכי מוקדם לעמותה:
                // ההגעה הסופית היא שעת הפתיחה הראשונה בטווח [הגעה בלי דחייה, הגעה בדחייה המקסימלית], וכל טווח מתחיל
                // אחרי תחילת הטווח של התחנות שלפניה. אם היא נפסלת (שבת, מרווח, המתנה) - בודקים את הבאה.
                for (int e = 0; e < ends; e++)
                {
                    int end = endOrder[e];
                    if (!TryTiming(input, parent, deadline, release, m, mask, end, service, maxWait,
                                   Math.Min(minExpiration[mask], availableMinutes), maxDelayByDeparture, path,
                                   out double delay, out double finalArrival))
                        continue;

                    bool better = value[mask] > bestValue + Eps ||
                                  (Math.Abs(value[mask] - bestValue) <= Eps && finalArrival < bestArrival - Eps);
                    if (better)
                    {
                        bestMask = mask;
                        bestEnd = end;
                        bestValue = value[mask];
                        bestArrival = finalArrival;
                        bestDelay = delay;
                    }
                    break;
                }
            }

            if (bestEnd < 0) return null;

            // ---------------- שחזור הסדר מהמצביעים, וזמני המסלול לפי שעת היציאה שנבחרה ----------------
            int count = ReconstructPath(parent, m, bestMask, bestEnd, path);
            var order = new List<int>(count);
            for (int k = 0; k < count; k++)
                order.Add(path[k]);

            var departure = t0.AddMinutes(bestDelay);
            var arrivals = new List<DateTime>(count);
            double drivingMinutes = 0, drivingKm = 0, waitMinutes = 0;
            double clock = bestDelay;   // דקות מ-t0
            int from = 0;
            foreach (int stop in order)
            {
                double reach = clock + input.Minutes[from, stop + 1];
                double pickup = Math.Max(reach, release[stop]);   // הגענו לפני שהתרומה מוכנה - מחכים
                waitMinutes += pickup - reach;
                arrivals.Add(t0.AddMinutes(pickup));
                drivingMinutes += input.Minutes[from, stop + 1];
                drivingKm += input.Km[from, stop + 1];
                clock = pickup + service;
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
                DrivingKm = drivingKm,
                WaitMinutes = waitMinutes
            };
        }

        /// <summary>
        /// תיקון 43: מתי לצאת במסלול (mask, end), ומתי מגיעים לעמותה. false = אין שעת יציאה שעומדת בכל האילוצים.
        ///
        /// לכל תחנה i במסלול, תחילת האיסוף כפונקציה של הדחייה d ביציאה היא b_i(d) = max(d + T_i, F_i):
        /// T_i = הנסיעה והשירות עד התחנה (בלי המתנות), F_i = הרגע המוקדם ביותר שמכתיבות שעות ה"זמינה החל מ-".
        /// מכאן, בלי ניחושים:
        ///   מועד האיסוף:   b_i(d) ≤ D_i                ⇔  d ≤ D_i - T_i
        ///   המתנה כוללת:   max(0, F_n - T_n - d) ≤ W     ⇔  d ≥ F_n - T_n - W
        ///   ההגעה לעמותה:  A(d) = max(d + T_ngo, F_ngo) - לא יורדת עם d, ולכן "עד שעה X" הוא חסם עליון על d.
        /// בטווח המותר נבחרת היציאה הראשונה שבה העמותה פתוחה בהגעה. אם המתנדב עדיין היה מחכה, היציאה נדחית עד
        /// שאין המתנה: כל עוד יש המתנה A(d) קבועה, ולכן ההגעה לעמותה (וכל האילוצים עליה) לא משתנה.
        /// בלי שעות "זמינה החל מ-" זה בדיוק החישוב הקודם: הגעה A(0), ודחייה רק כשהעמותה עוד סגורה.
        /// </summary>
        private static bool TryTiming(
            PlannerInput input, sbyte[] parent, double[] deadline, double[] release,
            int m, int mask, int end, double service, double maxWait, double latestNgoArrival, double maxDelayByDeparture,
            int[] path, out double delay, out double ngoArrival)
        {
            delay = 0;
            ngoArrival = double.PositiveInfinity;

            int count = ReconstructPath(parent, m, mask, end, path);

            // מעבר אחד קדימה: T, F, והמרווח של מועדי האיסוף (כמה אפשר לדחות את היציאה בלי לאחר לאף תחנה)
            double T = 0, F = double.NegativeInfinity, maxDelay = double.PositiveInfinity;
            int from = 0;
            for (int k = 0; k < count; k++)
            {
                int stop = path[k];
                double leg = input.Minutes[from, stop + 1];
                if (k > 0) { T += service; F += service; }
                T += leg;
                F = Math.Max(F + leg, release[stop]);
                maxDelay = Math.Min(maxDelay, deadline[stop] - T);
                from = stop + 1;
            }

            double toNgo = service + input.Minutes[from, m + 1];
            double tNgo = T + toNgo;
            double fNgo = F + toNgo;   // אין במסלול "זמינה החל מ-" = מינוס אינסוף

            // תפוגה וסוף המשמרת: A(d) ≤ X
            if (fNgo > latestNgoArrival + Eps) return false;
            maxDelay = Math.Min(maxDelay, latestNgoArrival - tNgo);
            maxDelay = Math.Min(maxDelay, Math.Min(maxDelayByDeparture, MaxDelayMinutes));

            double zeroWait = F - T;   // מהדחייה הזו ואילך המתנדב לא מחכה בכלל
            double minDelay = Math.Max(0, zeroWait - maxWait);
            if (maxDelay < minDelay - Eps) return false;
            if (maxDelay < minDelay) maxDelay = minDelay;

            var t0 = input.EarliestDepartureUtc;
            double fromArrival = Math.Max(minDelay + tNgo, fNgo);
            double toArrival = Math.Max(maxDelay + tNgo, fNgo);
            var open = input.EarliestNgoOpen(t0.AddMinutes(fromArrival), t0.AddMinutes(toArrival));
            if (!open.HasValue) return false;   // העמותה סגורה בכל הטווח האפשרי

            double openAt = (open.Value - t0).TotalMinutes;
            double d = openAt <= fromArrival + Eps
                ? minDelay
                : Math.Min(maxDelay, Math.Max(minDelay, openAt - tNgo));
            if (d < zeroWait)
                d = Math.Min(zeroWait, maxDelay);   // לא מחכים סתם: יוצאים מאוחר יותר ומגיעים לעמותה באותה שעה

            double arrival = Math.Max(d + tNgo, fNgo);
            if (input.IsArrivalBlocked?.Invoke(t0.AddMinutes(arrival)) == true) return false;

            delay = d;
            ngoArrival = arrival;
            return true;
        }

        // הסדר של המסלול שמסתיים ב-end אחרי ביקור בתחנות mask (מהמצביעים), מההתחלה לסוף. מחזיר את מספר התחנות.
        private static int ReconstructPath(sbyte[] parent, int m, int mask, int end, int[] path)
        {
            int count = 0;
            for (int state = mask, j = end; j >= 0;)
            {
                path[count++] = j;
                int previous = parent[state * m + j];
                state ^= 1 << j;
                j = previous;
            }
            Array.Reverse(path, 0, count);
            return count;
        }
    }
}
