using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>מתנדב מועמד יחד עם המרחק שלו לתרומה, ציון ההתאמה שלו וחלון הזמינות שבו ייסע.</summary>
    public class VolunteerCandidate
    {
        public Volunteer Volunteer { get; }
        public double DistanceKm { get; }
        public double Score { get; }
        public GeoCoordinate StartLocation { get; }
        public AvailabilityWindow Window { get; }

        public VolunteerCandidate(Volunteer volunteer, double distanceKm, double score = 0, GeoCoordinate? startLocation = null,
            AvailabilityWindow window = default)
        {
            Volunteer = volunteer ?? throw new ArgumentNullException(nameof(volunteer));
            DistanceKm = distanceKm;
            Score = score;
            StartLocation = startLocation ?? new GeoCoordinate();
            Window = window;
        }
    }

    /// <summary>מסלול שנבנה בהצלחה למתנדב מועמד</summary>
    public sealed class RouteCandidate
    {
        public VolunteerCandidate Candidate { get; init; } = null!;
        public RoutePlan Plan { get; init; } = null!;

        /// <summary>העמותה שאליה המסלול מגיע.</summary>
        public Ngo Ngo { get; init; } = null!;

        /// <summary>התרומה שבשבילה המסלול נבנה (העוגן).</summary>
        public int AnchorDonationId { get; init; }

        // Matching Score סופי של המסלול (0-100)
        public double Score { get; set; }

        /// <summary>
        /// תיקון 19: ערך המסלול לבחירה הגלובלית - "כמה מזון מציל המסלול" (ערך ה-Knapsack: ק"ג × דחיפות, עם הנחה
        /// על מרחק מהעוגן), כפול מקדם איכות של 0.85-1.0 לפי ה-Matching Score. כך הצלת מזון היא השיקול הראשי,
        /// ואיכות ההתאמה (מרחק, אמינות, יעילות) מכריעה בין חלופות דומות.
        /// </summary>
        public double Value => Plan.KnapsackValue * (0.85 + 0.15 * Math.Clamp(Score, 0, 100) / 100.0);
    }

    /// <summary>תוצאת הבחירה הגלובלית.</summary>
    public sealed class RouteSelection
    {
        public List<RouteCandidate> Routes { get; } = new();
        public double TotalValue { get; set; }

        /// <summary>true = החיפוש הסתיים במלואו, והפתרון אופטימלי (מבין המסלולים המועמדים).</summary>
        public bool ProvedOptimal { get; set; }
        public long NodesVisited { get; set; }
        public double GreedyValue { get; set; }

        /// <summary>חסם עליון מוכח על הערך האופטימלי (מבין המסלולים המועמדים). כשהחיפוש הסתיים - שווה ל-TotalValue.</summary>
        public double UpperBound { get; set; }

        /// <summary>המרחק המקסימלי האפשרי מהאופטימום, באחוזים. 0 = מוכח אופטימלי.</summary>
        public double OptimalityGapPercent => UpperBound <= 0 ? 0 : Math.Max(0, (UpperBound - TotalValue) / UpperBound * 100.0);
    }

    /// <summary>
    /// שלב 6 בטבלה - Backtracking + Branch &amp; Bound.
    ///
    /// FindRoutesAsync - מעבר על המתנדבים המדורגים: לכל אחד מנסים לבנות מסלול; אם אילוץ קשה נכשל - חוזרים אחורה
    /// וממשיכים למתנדב החלופי הבא, עד שנמצאו maxRoutes מסלולים אפשריים לתרומה.
    ///
    /// SelectRoutes (תיקון 19) - הבחירה הגלובלית בין המסלולים של כל התרומות בריצה. בעבר כל תרומה (לפי סדר
    /// הדחיפות) לקחה מיד את המסלול הטוב ביותר שלה - חמדני: מתנדב שהוא היחיד שמסוגל לקחת תרומה אחרת יכול
    /// "להישרף" על תרומה שהייתה לה חלופה, ואז תרומה שלמה לא נאספת. עכשיו - Branch &amp; Bound:
    ///
    ///   * Bound: הרפיית לגרנז' (פירוט במחלקה Search) - בעיית השמה מתנדבים × תרומות (השיטה ההונגרית) עם "מחירים"
    ///     לתרומות ולקיבולת העמותות. חסם עליון מוכח והדוק לערך האופטימלי.
    ///   * Branch: על מסלול שמעורב בהתנגשות (תרומה בשני מסלולים / עמותה מעל הקיבולת): "נבחר" או "לא נבחר".
    ///   * גיזום: ענף שהחסם שלו לא עובר את הפתרון הטוב ביותר שנמצא - נעצר.
    ///   * נקודת פתיחה: הפתרון החמדני (לפי סדר הדחיפות מה-Max-Heap), ולכן התוצאה לעולם לא גרועה ממנו.
    ///
    /// כשהחיפוש מסתיים - הפתרון מוכח כאופטימלי (מבין המסלולים המועמדים). אם הושגה תקרת הצמתים - נשמר הטוב שנמצא,
    /// ו-UpperBound / OptimalityGapPercent אומרים בדיוק כמה רחוק הוא יכול להיות מהאופטימום (במקרה הגרוע).
    /// </summary>
    public class BacktrackingAssignmentService
    {
        public Task<List<RouteCandidate>> FindRoutesAsync(
            IReadOnlyList<VolunteerCandidate> rankedCandidates,
            double maxDistanceKm,
            int maxRoutes,
            Func<VolunteerCandidate, Task<RoutePlan?>> tryBuildRoute)
        {
            if (tryBuildRoute == null) throw new ArgumentNullException(nameof(tryBuildRoute));
            return FindRouteCandidatesAsync(rankedCandidates, maxDistanceKm, maxRoutes,
                async c =>
                {
                    var plan = await tryBuildRoute(c);
                    return plan == null ? null : new RouteCandidate { Candidate = c, Plan = plan };
                });
        }

        /// <summary>כמו FindRoutesAsync, כשהבנייה מחזירה מסלול מלא (כולל העמותה שנבחרה).</summary>
        public Task<List<RouteCandidate>> FindRouteCandidatesAsync(
            IReadOnlyList<VolunteerCandidate> rankedCandidates,
            double maxDistanceKm,
            int maxRoutes,
            Func<VolunteerCandidate, Task<RouteCandidate?>> tryBuildRoute)
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
            Func<VolunteerCandidate, Task<RouteCandidate?>> tryBuildRoute,
            List<RouteCandidate> found)
        {
            // תנאי עצירה: עברנו על כל המועמדים, או שנמצאו מספיק מסלולים להשוואה
            if (index >= candidates.Count || found.Count >= maxRoutes)
                return found;

            var candidate = candidates[index];

            // Bound: מועמד מחוץ לטווח - לא בונים לו מסלול בכלל
            if (candidate.DistanceKm <= maxDistanceKm)
            {
                var route = await tryBuildRoute(candidate);
                if (route != null)
                    found.Add(route);
            }

            // Backtrack: ממשיכים למועמד הבא
            return await SearchAsync(candidates, index + 1, maxRoutes, maxDistanceKm, tryBuildRoute, found);
        }

        // =========================================================
        // תיקון 19: Branch & Bound גלובלי
        // =========================================================

        private sealed class Option
        {
            public RouteCandidate Route = null!;
            public int Index;           // מיקום ברשימת כל המסלולים
            public int Anchor;          // אינדקס תרומת העוגן (= מיקומה בסדר הדחיפות)
            public int Volunteer;       // אינדקס מתנדב
            public int[] Donations = Array.Empty<int>();
            public int Ngo;             // אינדקס עמותה עם מגבלת קיבולת, -1 = ללא הגבלה
            public double WeightKg;
            public double Value;
        }

        public RouteSelection SelectRoutes(
            IReadOnlyList<int> anchorOrder,
            IReadOnlyDictionary<int, IReadOnlyList<RouteCandidate>> routesByAnchor,
            IReadOnlyDictionary<int, double> ngoRemainingKg,
            long nodeLimit = 100_000,
            TimeSpan? timeLimit = null)
        {
            if (anchorOrder == null) throw new ArgumentNullException(nameof(anchorOrder));
            if (routesByAnchor == null) throw new ArgumentNullException(nameof(routesByAnchor));
            ngoRemainingKg ??= new Dictionary<int, double>();

            // ---------- הכנה: אינדקסים לתרומות / מתנדבים / עמותות ----------
            var anchors = anchorOrder
                .Distinct()
                .Where(id => routesByAnchor.TryGetValue(id, out var r) && r != null && r.Count > 0)
                .ToList();

            var donationIndex = new Dictionary<int, int>();
            var volunteerIndex = new Dictionary<int, int>();
            var ngoIndex = new Dictionary<int, int>();
            var ngoCapacity = new List<double>();
            int IndexOf(Dictionary<int, int> map, int id) => map.TryGetValue(id, out var i) ? i : map[id] = map.Count;

            // תרומות העוגן מקבלות ראשונות את האינדקסים 0..A-1 (לפי סדר הדחיפות)
            foreach (var id in anchors) IndexOf(donationIndex, id);

            var options = new List<Option>();
            var byAnchor = new List<Option>[anchors.Count];
            for (int a = 0; a < anchors.Count; a++)
            {
                byAnchor[a] = new List<Option>();
                var ordered = routesByAnchor[anchors[a]]
                    .Where(r => r?.Plan != null && r.Candidate != null && r.Ngo != null &&
                                r.Plan.Stops.Any(s => s.DonationId == anchors[a]))
                    .OrderByDescending(r => r.Value)
                    .ThenBy(r => r.Plan.TotalDurationMinutes);

                foreach (var r in ordered)
                {
                    int ngo = -1;
                    if (ngoRemainingKg.TryGetValue(r.Ngo.Id, out var left))
                    {
                        if (!ngoIndex.TryGetValue(r.Ngo.Id, out ngo))
                        {
                            ngo = ngoIndex[r.Ngo.Id] = ngoCapacity.Count;
                            ngoCapacity.Add(left);
                        }
                    }

                    var option = new Option
                    {
                        Route = r,
                        Index = options.Count,
                        Anchor = a,
                        Volunteer = IndexOf(volunteerIndex, r.Candidate.Volunteer.Id),
                        Donations = r.Plan.Stops.Select(s => IndexOf(donationIndex, s.DonationId)).Distinct().ToArray(),
                        Ngo = ngo,
                        WeightKg = r.Plan.TotalWeightKg,
                        Value = Math.Max(0, r.Value)
                    };
                    options.Add(option);
                    byAnchor[a].Add(option);
                }
            }

            var search = new Search(options, byAnchor, donationIndex.Count, volunteerIndex.Count, ngoCapacity.ToArray(),
                Math.Max(1, nodeLimit), timeLimit ?? TimeSpan.FromSeconds(10));

            // ---------- נקודת פתיחה: החמדני (לפי סדר הדחיפות, המסלול הטוב שלא מתנגש) ----------
            search.SeedWithGreedy();
            double greedyValue = search.BestValue;

            // ---------- Branch & Bound ----------
            search.Run();

            var selection = new RouteSelection
            {
                TotalValue = search.BestValue,
                GreedyValue = greedyValue,
                ProvedOptimal = !search.Truncated,
                NodesVisited = search.Nodes,
                // הסתיים: הטוב שנמצא הוא האופטימום. נעצר בתקרה: ההרפיה בשורש היא חסם עליון מוכח.
                UpperBound = search.Truncated ? Math.Max(search.BestValue, search.RootBound) : search.BestValue
            };
            // לפי סדר הדחיפות - כך נשמרים קודם המסלולים של התרומות הדחופות
            selection.Routes.AddRange(search.Best.OrderBy(o => o.Anchor).Select(o => o.Route));
            return selection;
        }

        /// <summary>
        /// ה-Branch &amp; Bound עצמו. החסם בכל צומת - הרפיית לגרנז' (Lagrangian Relaxation):
        ///
        ///   האילוצים "כל תרומה במסלול אחד לכל היותר" ו"לא לחרוג מקיבולת העמותה" יוצאים מהבעיה, ובמקומם כל תרומה
        ///   מקבלת "מחיר" λ ≥ 0 וכל ק"ג בעמותה מוגבלת מחיר μ ≥ 0. מה שנשאר הוא בעיית השמה (מתנדב ↔ תרומה-עוגן)
        ///   עם ערכים מופחתים: ערך המסלול פחות המחירים של התרומות שבו - נפתרת במדויק בשיטה ההונגרית.
        ///     L(λ, μ) = ההשמה הטובה ביותר בערכים המופחתים + Σ λ + Σ μ · (הקיבולת שנשארה)
        ///   לכל מחירים אי-שליליים L הוא חסם עליון מוכח לפתרון האופטימלי. את המחירים מכווננים בצעדי תת-גרדיאנט:
        ///   תרומה שנספרה בשני מסלולים - המחיר שלה עולה; תרומה שלא נאספה - יורד (Held-Karp, Fisher).
        ///   בלי המחירים (ההשמה "הנאיבית") תחנה משותפת לשני מסלולים נספרת פעמיים, והחסם רופף בעשרות אחוזים.
        ///
        ///   בכל צעד גם "מתקנים" את פתרון ההרפיה לפתרון חוקי (מורידים מסלולים מתנגשים ומשלימים בחמדנות) - וכך
        ///   הפתרון הטוב ביותר (החסם התחתון) משתפר כל הזמן. כשהחסם העליון של צומת ≤ הפתרון הטוב - הצומת נגזם.
        ///
        ///   פיצול: על מסלול מתוך פתרון ההרפיה (קודם מסלול שמעורב בהתנגשות): "נבחר" / "לא נבחר".
        /// </summary>
        private sealed class Search
        {
            private const double Eps = 1e-9;
            private const int RootIterations = 150;   // בשורש: כיוונון מלא של המחירים
            private const int NodeIterations = 10;    // בצמתים: המשך מהמחירים של ההורה (warm start)

            private readonly List<Option> _options;
            private readonly List<Option>[] _byAnchor;
            private readonly int _donations;
            private readonly int _volunteers;
            private readonly double[] _ngoCapacity;
            private readonly long _nodeLimit;
            private readonly DateTime _deadline;
            private readonly bool[] _forced;
            private readonly bool[] _excluded;
            private readonly double[] _lambda;   // מחיר לכל תרומה
            private readonly double[] _mu;       // מחיר לכל ק"ג בכל עמותה מוגבלת

            public List<Option> Best { get; private set; } = new();
            public double BestValue { get; private set; }
            public long Nodes { get; private set; }
            public bool Truncated { get; private set; }
            public double RootBound { get; private set; } = double.PositiveInfinity;

            public Search(List<Option> options, List<Option>[] byAnchor, int donations, int volunteers, double[] ngoCapacity,
                long nodeLimit, TimeSpan timeLimit)
            {
                _options = options;
                _byAnchor = byAnchor;
                _donations = donations;
                _volunteers = volunteers;
                _ngoCapacity = ngoCapacity;
                _nodeLimit = nodeLimit;
                _deadline = DateTime.UtcNow + timeLimit;
                _forced = new bool[options.Count];
                _excluded = new bool[options.Count];
                _lambda = new double[donations];
                _mu = new double[ngoCapacity.Length];
            }

            // מצב: אילו מתנדבים תפוסים, אילו תרומות נלקחו, כמה מקום נשאר בכל עמותה
            private sealed class State
            {
                public bool[] VolunteerUsed = null!;
                public bool[] DonationTaken = null!;
                public double[] NgoLeft = null!;

                public State Clone() => new()
                {
                    VolunteerUsed = (bool[])VolunteerUsed.Clone(),
                    DonationTaken = (bool[])DonationTaken.Clone(),
                    NgoLeft = (double[])NgoLeft.Clone()
                };
            }

            private State NewState() => new()
            {
                VolunteerUsed = new bool[_volunteers],
                DonationTaken = new bool[_donations],
                NgoLeft = (double[])_ngoCapacity.Clone()
            };

            private static bool Fits(Option o, State s)
            {
                if (s.VolunteerUsed[o.Volunteer]) return false;
                foreach (var d in o.Donations)
                    if (s.DonationTaken[d]) return false;
                return o.Ngo < 0 || s.NgoLeft[o.Ngo] + Eps >= o.WeightKg;
            }

            private static void Take(Option o, State s)
            {
                s.VolunteerUsed[o.Volunteer] = true;
                foreach (var d in o.Donations) s.DonationTaken[d] = true;
                if (o.Ngo >= 0) s.NgoLeft[o.Ngo] -= o.WeightKg;
            }

            public void SeedWithGreedy()
            {
                var state = NewState();
                var chosen = new List<Option>();
                double value = 0;

                for (int a = 0; a < _byAnchor.Length; a++)
                {
                    if (state.DonationTaken[a]) continue;   // נאספה כבר כתחנה במסלול של תרומה דחופה יותר
                    var option = _byAnchor[a].FirstOrDefault(o => Fits(o, state));
                    if (option == null) continue;
                    Take(option, state);
                    chosen.Add(option);
                    value += option.Value;
                }

                Best = chosen;
                BestValue = value;
            }

            public void Run() => Branch();

            private void Branch()
            {
                if (Truncated) return;
                if (++Nodes > _nodeLimit || (Nodes % 16 == 0 && DateTime.UtcNow > _deadline)) { Truncated = true; return; }
                bool root = Nodes == 1;

                // ---------- המסלולים שכבר "נבחרו" בענף הזה ----------
                var state = NewState();
                var forcedRoutes = new List<Option>();
                double forcedValue = 0;
                foreach (var o in _options)
                {
                    if (!_forced[o.Index]) continue;
                    if (!Fits(o, state)) return;   // לא אמור לקרות - מסלולים שנבחרו לא מתנגשים
                    Take(o, state);
                    forcedRoutes.Add(o);
                    forcedValue += o.Value;
                }

                // ---------- המסלולים שעוד אפשריים בענף ----------
                var candidates = _options
                    .Where(o => !_forced[o.Index] && !_excluded[o.Index] && Fits(o, state))
                    .OrderByDescending(o => o.Value)
                    .ToList();

                if (candidates.Count == 0)
                {
                    Offer(forcedRoutes, forcedValue);
                    if (root) RootBound = forcedValue;
                    return;
                }

                // שורה לכל תרומת-עוגן, עמודה לכל מתנדב
                var rowOf = new Dictionary<int, int>();
                var colOf = new Dictionary<int, int>();
                var cell = new (int Row, int Col)[candidates.Count];
                for (int i = 0; i < candidates.Count; i++)
                {
                    var o = candidates[i];
                    if (!rowOf.TryGetValue(o.Anchor, out int row)) row = rowOf[o.Anchor] = rowOf.Count;
                    if (!colOf.TryGetValue(o.Volunteer, out int col)) col = colOf[o.Volunteer] = colOf.Count;
                    cell[i] = (row, col);
                }

                var weights = new double[rowOf.Count, colOf.Count];
                var choice = new int[rowOf.Count, colOf.Count];
                var coverage = new int[_donations];
                var load = new double[_ngoCapacity.Length];

                double nodeBound = double.PositiveInfinity;
                List<Option> relaxed = new();
                double theta = root ? 2.0 : 0.25;
                int sinceImprovement = 0;
                int iterations = root ? RootIterations : NodeIterations;

                for (int k = 0; k < iterations; k++)
                {
                    // ---- 1. השמה אופטימלית בערכים המופחתים (ערך המסלול פחות מחירי התרומות שבו) ----
                    Array.Clear(weights);
                    for (int r = 0; r < choice.GetLength(0); r++)
                        for (int c = 0; c < choice.GetLength(1); c++)
                            choice[r, c] = -1;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var o = candidates[i];
                        double reduced = o.Value;
                        foreach (var d in o.Donations) reduced -= _lambda[d];
                        if (o.Ngo >= 0) reduced -= _mu[o.Ngo] * o.WeightKg;

                        var (row, col) = cell[i];
                        if (reduced > weights[row, col]) { weights[row, col] = reduced; choice[row, col] = i; }
                    }

                    var (matched, assignment) = HungarianAlgorithm.MaxWeightMatching(weights);
                    var x = new List<Option>();
                    for (int row = 0; row < assignment.Length; row++)
                        if (assignment[row] >= 0 && choice[row, assignment[row]] >= 0)
                            x.Add(candidates[choice[row, assignment[row]]]);

                    // ---- 2. החסם העליון L(λ, μ) ----
                    double bound = forcedValue + matched;
                    for (int d = 0; d < _donations; d++)
                        if (!state.DonationTaken[d]) bound += _lambda[d];
                    for (int n = 0; n < _mu.Length; n++)
                        bound += _mu[n] * Math.Max(0, state.NgoLeft[n]);

                    if (bound < nodeBound - Eps) { nodeBound = bound; relaxed = x; sinceImprovement = 0; }
                    else if (++sinceImprovement >= 5) { theta = Math.Max(1e-4, theta / 2); sinceImprovement = 0; }

                    // ---- 3. פתרון חוקי מתוך ההרפיה (חסם תחתון) ----
                    Repair(x, candidates, state, forcedRoutes, forcedValue);

                    if (nodeBound <= BestValue + Eps)
                        break;   // גיזום

                    // ---- 4. צעד תת-גרדיאנט על המחירים ----
                    Array.Clear(coverage);
                    Array.Clear(load);
                    foreach (var o in x)
                    {
                        foreach (var d in o.Donations) coverage[d]++;
                        if (o.Ngo >= 0) load[o.Ngo] += o.WeightKg;
                    }

                    double norm = 0;
                    for (int d = 0; d < _donations; d++)
                    {
                        if (state.DonationTaken[d]) continue;
                        double g = 1 - coverage[d];
                        if (g > 0 && _lambda[d] <= 0) continue;   // כבר 0 - לא יורד יותר
                        norm += g * g;
                    }
                    for (int n = 0; n < _mu.Length; n++)
                    {
                        double g = Math.Max(0, state.NgoLeft[n]) - load[n];
                        if (g > 0 && _mu[n] <= 0) continue;
                        norm += g * g;
                    }
                    if (norm < Eps)
                        break;   // ההרפיה כבר חוקית ומתאימה למחירים - הצומת נפתר

                    double step = theta * (bound - BestValue) / norm;
                    for (int d = 0; d < _donations; d++)
                    {
                        if (state.DonationTaken[d]) continue;
                        _lambda[d] = Math.Max(0, _lambda[d] - step * (1 - coverage[d]));
                    }
                    for (int n = 0; n < _mu.Length; n++)
                        _mu[n] = Math.Max(0, _mu[n] - step * (Math.Max(0, state.NgoLeft[n]) - load[n]));
                }

                if (root) RootBound = nodeBound;
                if (nodeBound <= BestValue + Eps)
                    return;

                // ---------- Branch: מסלול שמעורב בהתנגשות (ואם אין - המסלול החשוב בהרפיה): נבחר / לא נבחר ----------
                var pivot = FindConflict(forcedRoutes.Concat(relaxed).ToList())
                            ?? relaxed.OrderByDescending(o => o.Value).FirstOrDefault()
                            ?? candidates[0];

                // המחירים ממשיכים מענף לענף (warm start) - נמדד: מוכיח אופטימליות בפחות צמתים מאשר שחזור לכל ענף
                _forced[pivot.Index] = true;
                Branch();
                _forced[pivot.Index] = false;
                if (Truncated) return;

                _excluded[pivot.Index] = true;
                Branch();
                _excluded[pivot.Index] = false;
            }

            // מפתרון ההרפיה לפתרון חוקי: המסלולים שלו לפי ערך (מוותרים על המתנגשים), ואז השלמה חמדנית מכל השאר
            private void Repair(List<Option> relaxed, List<Option> candidatesByValue, State state, List<Option> forcedRoutes, double forcedValue)
            {
                var s = state.Clone();
                var chosen = new List<Option>(forcedRoutes);
                double value = forcedValue;

                foreach (var o in relaxed.OrderByDescending(o => o.Value))
                {
                    if (!Fits(o, s)) continue;
                    Take(o, s);
                    chosen.Add(o);
                    value += o.Value;
                }

                foreach (var o in candidatesByValue)
                {
                    if (!Fits(o, s)) continue;
                    Take(o, s);
                    chosen.Add(o);
                    value += o.Value;
                }

                Offer(chosen, value);
            }

            private void Offer(List<Option> solution, double value)
            {
                if (value <= BestValue + Eps) return;
                BestValue = value;
                Best = new List<Option>(solution);
            }

            // התנגשות: תרומה בשני מסלולים, או עמותה שחורגת מהקיבולת שלה.
            // מחזיר מסלול (שעוד לא נקבע בענף) להתפצל עליו - בעל הערך הגבוה מבין המעורבים. null = אין התנגשות.
            private Option? FindConflict(List<Option> selected)
            {
                var owner = new Option?[_donations];
                foreach (var o in selected)
                {
                    foreach (var d in o.Donations)
                    {
                        var other = owner[d];
                        if (other == null) { owner[d] = o; continue; }
                        if (_forced[other.Index]) return o;
                        if (_forced[o.Index]) return other;
                        return o.Value > other.Value ? o : other;
                    }
                }

                if (_ngoCapacity.Length > 0)
                {
                    var used = new double[_ngoCapacity.Length];
                    foreach (var o in selected)
                        if (o.Ngo >= 0) used[o.Ngo] += o.WeightKg;

                    for (int n = 0; n < used.Length; n++)
                    {
                        if (used[n] <= _ngoCapacity[n] + Eps) continue;
                        var pivot = selected
                            .Where(o => o.Ngo == n && !_forced[o.Index])
                            .OrderByDescending(o => o.Value)
                            .FirstOrDefault();
                        if (pivot != null) return pivot;
                    }
                }

                return null;
            }
        }
    }
}
