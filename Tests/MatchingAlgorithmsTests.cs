using BLL.Algorithms;
using BLL.Configuration;
using BLL.Services;
using DAL;
using DAL.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    /// <summary>
    /// בדיקות נכונות לאלגוריתמים של מנוע השיבוץ: כל אלגוריתם מושווה ל"כוח גס" (מעבר על כל האפשרויות)
    /// על אלפי מקרים אקראיים (זרע קבוע - תוצאה זהה בכל הרצה).
    /// </summary>
    public class MatchingAlgorithmsTests
    {
        private static readonly DateTime T0 = new(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc);

        // =====================================================================
        // RoutePlanner (DP על תתי-קבוצות) מול מעבר על כל תתי-הקבוצות וכל הסדרים
        // =====================================================================

        private sealed record Brute(double Value, double Arrival);

        private static Brute? BruteForceRoute(PlannerInput input)
        {
            int m = input.Stops.Count, ngo = m + 1;
            Brute? best = null;

            for (int mask = 1; mask < (1 << m); mask++)
            {
                if ((mask & (1 << input.AnchorIndex)) == 0) continue;
                var set = Enumerable.Range(0, m).Where(i => (mask & (1 << i)) != 0).ToList();
                if (set.Sum(i => input.Stops[i].WeightKg) > input.CapacityKg + 1e-9) continue;

                double bestArrival = double.PositiveInfinity;
                foreach (var order in Permutations(set))
                {
                    double t = 0;
                    int from = 0;
                    bool ok = true;
                    foreach (var stop in order)
                    {
                        t += input.Minutes[from, stop + 1];
                        // תיקון 43: הגענו לפני "זמינה החל מ-" - מחכים
                        var ready = input.Stops[stop].EarliestPickupUtc;
                        if (ready.HasValue && T0.AddMinutes(t) < ready.Value) t = (ready.Value - T0).TotalMinutes;
                        if (T0.AddMinutes(t) > input.Stops[stop].PickupDeadlineUtc.AddTicks(1)) { ok = false; break; }
                        t += input.ServiceMinutes;
                        from = stop + 1;
                    }
                    if (!ok) continue;
                    double arrival = t + input.Minutes[from, ngo];
                    bestArrival = Math.Min(bestArrival, arrival);
                }

                if (double.IsPositiveInfinity(bestArrival)) continue;
                var arrivalUtc = T0.AddMinutes(bestArrival);
                if (set.Any(i => arrivalUtc > input.Stops[i].ExpirationUtc.AddTicks(1))) continue;
                if (arrivalUtc > input.AvailableUntilUtc.AddTicks(1)) continue;

                double value = set.Sum(i => input.Stops[i].Value);
                if (best == null || value > best.Value + 1e-9 || (Math.Abs(value - best.Value) <= 1e-9 && bestArrival < best.Arrival - 1e-9))
                    best = new Brute(value, bestArrival);
            }

            return best;
        }

        private static IEnumerable<List<int>> Permutations(List<int> items)
        {
            if (items.Count <= 1) { yield return new List<int>(items); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                var rest = items.Where((_, j) => j != i).ToList();
                foreach (var p in Permutations(rest))
                {
                    p.Insert(0, items[i]);
                    yield return p;
                }
            }
        }

        // withEarliestPickup (תיקון 43): חצי מהתחנות מוכנות רק משעה מסוימת, ואפשר לדחות את היציאה עד 55 דקות
        private static PlannerInput RandomRoute(Random rnd, int m, bool withEarliestPickup = false)
        {
            var stops = Enumerable.Range(0, m).Select(i =>
            {
                var deadline = T0.AddMinutes(rnd.Next(5, 120));
                DateTime? ready = null;
                if (withEarliestPickup && rnd.Next(2) == 0)
                {
                    var from = T0.AddMinutes(rnd.Next(-20, 100));
                    ready = from > deadline.AddMinutes(-5) ? deadline.AddMinutes(-5) : from;
                }

                return new PlannerStop
                {
                    DonationId = i + 1,
                    PickupDeadlineUtc = deadline,
                    EarliestPickupUtc = ready,
                    ExpirationUtc = T0.AddMinutes(rnd.Next(30, 240)),
                    Value = rnd.Next(1, 10),
                    WeightKg = rnd.Next(1, 10)
                };
            }).ToList();

            int size = m + 2;
            var minutes = new double[size, size];
            var km = new double[size, size];
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                    if (i != j) { minutes[i, j] = rnd.Next(1, 30); km[i, j] = minutes[i, j] / 2; }

            return new PlannerInput
            {
                Stops = stops,
                AnchorIndex = rnd.Next(m),
                Minutes = minutes,
                Km = km,
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = withEarliestPickup ? T0.AddMinutes(rnd.Next(0, 56)) : T0,
                AvailableUntilUtc = T0.AddMinutes(rnd.Next(20, 200)),
                ServiceMinutes = rnd.Next(0, 6),
                CapacityKg = rnd.Next(5, 40)
            };
        }

        [Fact]
        public void RoutePlanner_FindsTheSameOptimumAsBruteForce_On3000RandomRoutes()
        {
            var rnd = new Random(20261019);
            for (int iteration = 0; iteration < 3000; iteration++)
            {
                var input = RandomRoute(rnd, rnd.Next(1, 7));
                var expected = BruteForceRoute(input);
                var actual = RoutePlanner.Plan(input);

                if (expected == null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.Equal(expected.Value, actual!.Value, 6);
                Assert.Equal(expected.Arrival, (actual.NgoArrivalUtc - T0).TotalMinutes, 4);
                AssertRouteIsFeasible(input, actual);
            }
        }

        // תיקון 43: תחנות שמוכנות רק משעה מסוימת ("זמינה החל מ-") ויציאה שאפשר לדחות. כשמותר לחכות, הגעה מוקדמת
        // אף פעם לא מזיקה - וה-DP נשאר מדויק: אותו ערך ואותה הגעה לעמותה כמו המעבר על כל הקבוצות וכל הסדרים.
        [Fact]
        public void RoutePlanner_WithEarliestPickupTimes_FindsTheSameOptimumAsBruteForce_On3000RandomRoutes()
        {
            var rnd = new Random(20261110);
            for (int iteration = 0; iteration < 3000; iteration++)
            {
                var input = RandomRoute(rnd, rnd.Next(1, 7), withEarliestPickup: true);
                var expected = BruteForceRoute(input);
                var actual = RoutePlanner.Plan(input);

                if (expected == null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.Equal(expected.Value, actual!.Value, 6);
                Assert.Equal(expected.Arrival, (actual.NgoArrivalUtc - T0).TotalMinutes, 4);
                AssertRouteIsFeasible(input, actual);
            }
        }

        // בדיקה עצמאית של המסלול שהוחזר: מחשבים מחדש את הזמנים לפי הסדר ובודקים כל אילוץ
        private static void AssertRouteIsFeasible(PlannerInput input, PlannerResult route)
        {
            Assert.Contains(input.AnchorIndex, route.Order);
            Assert.Equal(route.Order.Count, route.Order.Distinct().Count());
            Assert.True(route.Order.Sum(i => input.Stops[i].WeightKg) <= input.CapacityKg + 1e-9);

            Assert.True(route.DepartureUtc >= input.EarliestDepartureUtc.AddSeconds(-1));
            Assert.True(route.DepartureUtc <= input.LatestDepartureUtc.AddSeconds(1));

            var t = route.DepartureUtc;
            double waited = 0;
            int from = 0;
            for (int k = 0; k < route.Order.Count; k++)
            {
                int stop = route.Order[k];
                t = t.AddMinutes(input.Minutes[from, stop + 1]);
                // תיקון 43: לא אוספים לפני "זמינה החל מ-" - מחכים
                var ready = input.Stops[stop].EarliestPickupUtc;
                if (ready.HasValue && t < ready.Value)
                {
                    waited += (ready.Value - t).TotalMinutes;
                    t = ready.Value;
                }
                Assert.True(Math.Abs((t - route.ArrivalsUtc[k]).TotalSeconds) < 1);
                Assert.True(t <= input.Stops[stop].PickupDeadlineUtc.AddSeconds(1));
                t = t.AddMinutes(input.ServiceMinutes);
                from = stop + 1;
            }
            t = t.AddMinutes(input.Minutes[from, input.Stops.Count + 1]);

            Assert.Equal(waited, route.WaitMinutes, 3);
            Assert.True(waited <= input.MaxWaitMinutes + 1e-6);

            Assert.True(Math.Abs((t - route.NgoArrivalUtc).TotalSeconds) < 1);
            if (input.AvailableUntilUtc != DateTime.MaxValue)
                Assert.True(route.NgoArrivalUtc <= input.AvailableUntilUtc.AddSeconds(1));
            Assert.All(route.Order, i => Assert.True(route.NgoArrivalUtc <= input.Stops[i].ExpirationUtc.AddSeconds(1)));
        }

        [Fact]
        public void RoutePlanner_NearestNeighbourTrap_TakesTheUrgentFarStopFirst()
        {
            // נקודת יציאה 0; תחנה A קרובה (2 דק') עם זמן מאוחר; תחנה B רחוקה (15 דק') שחייבת להיאסף תוך 16 דקות
            var input = new PlannerInput
            {
                Stops = new[]
                {
                    new PlannerStop { DonationId = 1, PickupDeadlineUtc = T0.AddHours(3), ExpirationUtc = T0.AddHours(5), Value = 1, WeightKg = 1 },
                    new PlannerStop { DonationId = 2, PickupDeadlineUtc = T0.AddMinutes(16), ExpirationUtc = T0.AddHours(5), Value = 1, WeightKg = 1 }
                },
                AnchorIndex = 1,
                Minutes = new double[,] { { 0, 2, 15, 0 }, { 2, 0, 14, 10 }, { 15, 14, 0, 10 }, { 0, 0, 0, 0 } },
                Km = new double[4, 4],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0,
                ServiceMinutes = 5
            };

            var route = RoutePlanner.Plan(input);

            Assert.NotNull(route);
            Assert.Equal(new[] { 1, 0 }, route!.Order);   // B ואז A - שתיהן נאספות
        }

        [Fact]
        public void RoutePlanner_DelaysTheDeparture_UntilTheNgoOpens_WithinTheSlack()
        {
            // נסיעה 10 דק' לתחנה (מועד אחרון +50), 5 דק' שירות, 10 דק' לעמותה: הגעה ב-+25. העמותה נפתחת ב-+60.
            PlannerInput Input(int opensAfterMinutes) => new()
            {
                Stops = new[] { new PlannerStop { DonationId = 1, PickupDeadlineUtc = T0.AddMinutes(50), ExpirationUtc = T0.AddHours(5), Value = 1, WeightKg = 1 } },
                AnchorIndex = 0,
                Minutes = new double[,] { { 0, 10, 0 }, { 0, 0, 10 }, { 0, 0, 0 } },
                Km = new double[3, 3],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0.AddHours(1),
                ServiceMinutes = 5,
                EarliestNgoOpen = (from, to) =>
                {
                    var opens = T0.AddMinutes(opensAfterMinutes);
                    var t = from > opens ? from : opens;
                    return t <= to ? t : null;
                }
            };

            var delayed = RoutePlanner.Plan(Input(60));
            Assert.NotNull(delayed);
            Assert.Equal(T0.AddMinutes(35), delayed!.DepartureUtc);       // דחייה של 35 דקות
            Assert.Equal(T0.AddMinutes(60), delayed.NgoArrivalUtc);
            Assert.True(delayed.ArrivalsUtc[0] <= T0.AddMinutes(50));      // ועדיין לפני מועד האיסוף

            // נפתחת ב-+80: דחייה של 55 דקות תאחר את האיסוף (מרווח של 40 בלבד) - אין מסלול
            Assert.Null(RoutePlanner.Plan(Input(80)));
        }

        [Fact]
        public void RoutePlanner_DropsOnlyTheStopsThatDoNotFit_NotTheWholeRoute()
        {
            // העוגן קרוב; שתי תחנות נוספות רחוקות. המתנדב זמין רק 30 דקות - נשאר העוגן + מה שנכנס
            var input = new PlannerInput
            {
                Stops = new[]
                {
                    new PlannerStop { DonationId = 1, PickupDeadlineUtc = T0.AddHours(1), ExpirationUtc = T0.AddHours(5), Value = 5, WeightKg = 1 },
                    new PlannerStop { DonationId = 2, PickupDeadlineUtc = T0.AddHours(3), ExpirationUtc = T0.AddHours(5), Value = 3, WeightKg = 1 },
                    new PlannerStop { DonationId = 3, PickupDeadlineUtc = T0.AddHours(3), ExpirationUtc = T0.AddHours(5), Value = 3, WeightKg = 1 }
                },
                AnchorIndex = 0,
                Minutes = new double[,]
                {
                    { 0, 1, 20, 20, 0 },
                    { 1, 0, 20, 20, 2 },
                    { 20, 20, 0, 30, 20 },
                    { 20, 20, 30, 0, 20 },
                    { 0, 0, 0, 0, 0 }
                },
                Km = new double[5, 5],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0,
                AvailableUntilUtc = T0.AddMinutes(30),
                ServiceMinutes = 2
            };

            var route = RoutePlanner.Plan(input);

            Assert.NotNull(route);
            Assert.Equal(new[] { 0 }, route!.Order);
            Assert.Equal(5, route.Value);
        }

        [Fact]
        public void RoutePlanner_RespectsABlockedArrival_SuchAsShabbat()
        {
            var input = new PlannerInput
            {
                Stops = new[] { new PlannerStop { DonationId = 1, PickupDeadlineUtc = T0.AddHours(1), ExpirationUtc = T0.AddHours(5), Value = 1, WeightKg = 1 } },
                AnchorIndex = 0,
                Minutes = new double[,] { { 0, 10, 0 }, { 0, 0, 10 }, { 0, 0, 0 } },
                Km = new double[3, 3],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0,
                IsArrivalBlocked = arrival => arrival > T0.AddMinutes(15)
            };

            Assert.Null(RoutePlanner.Plan(input));
        }

        [Fact]
        public void RoutePlanner_DonationAvailableOnlyLater_LeavesLaterInsteadOfWaiting()
        {
            // תיקון 43: נסיעה 10 דק' לתחנה שמוכנה רק ב-+40 (מועד אחרון +90), 5 דק' שירות, 10 דק' לעמותה.
            // אפשר לצאת עד +60. במקום לצאת עכשיו ולחכות חצי שעה - יוצאים ב-+30 ומגיעים בדיוק כשהתרומה מוכנה.
            var input = new PlannerInput
            {
                Stops = new[]
                {
                    new PlannerStop
                    {
                        DonationId = 1, PickupDeadlineUtc = T0.AddMinutes(90), EarliestPickupUtc = T0.AddMinutes(40),
                        ExpirationUtc = T0.AddHours(5), Value = 1, WeightKg = 1
                    }
                },
                AnchorIndex = 0,
                Minutes = new double[,] { { 0, 10, 0 }, { 0, 0, 10 }, { 0, 0, 0 } },
                Km = new double[3, 3],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0.AddHours(1),
                ServiceMinutes = 5,
                MaxWaitMinutes = 20
            };

            var route = RoutePlanner.Plan(input);

            Assert.NotNull(route);
            Assert.Equal(T0.AddMinutes(30), route!.DepartureUtc);
            Assert.Equal(T0.AddMinutes(40), route.ArrivalsUtc[0]);       // לא לפני "זמינה החל מ-"
            Assert.Equal(0, route.WaitMinutes, 6);
            Assert.Equal(T0.AddMinutes(55), route.NgoArrivalUtc);
            AssertRouteIsFeasible(input, route);

            // אם אי אפשר לצאת אחרי +5: הגעה ב-+15 ו-25 דקות המתנה - יותר ממה שמותר (20), ולכן אין מסלול
            var cannotWait = new PlannerInput
            {
                Stops = input.Stops,
                AnchorIndex = 0,
                Minutes = input.Minutes,
                Km = input.Km,
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0.AddMinutes(5),
                ServiceMinutes = 5,
                MaxWaitMinutes = 20
            };
            Assert.Null(RoutePlanner.Plan(cannotWait));
        }

        [Fact]
        public void RoutePlanner_DoesNotCombineAnEarlyStopWithALateOne_WhenTheWaitIsTooLong()
        {
            // תיקון 43: העוגן A חייב להיאסף עד +20 (נסיעה 10 דק'). B מוכנה רק ב-+60 (נסיעה A→B 10 דק').
            // יחד: איסוף A ב-+15 לכל המאוחר (יציאה עד +5), הגעה ל-B ב-+30 - ו-30 דקות המתנה.
            PlannerInput Input(double maxWait) => new()
            {
                Stops = new[]
                {
                    new PlannerStop { DonationId = 1, PickupDeadlineUtc = T0.AddMinutes(20), ExpirationUtc = T0.AddHours(5), Value = 5, WeightKg = 1 },
                    new PlannerStop
                    {
                        DonationId = 2, PickupDeadlineUtc = T0.AddMinutes(120), EarliestPickupUtc = T0.AddMinutes(60),
                        ExpirationUtc = T0.AddHours(5), Value = 3, WeightKg = 1
                    }
                },
                AnchorIndex = 0,
                Minutes = new double[,]
                {
                    { 0, 10, 10, 0 },
                    { 0, 0, 10, 10 },
                    { 0, 10, 0, 10 },
                    { 0, 0, 0, 0 }
                },
                Km = new double[4, 4],
                EarliestDepartureUtc = T0,
                LatestDepartureUtc = T0.AddMinutes(5),
                ServiceMinutes = 5,
                MaxWaitMinutes = maxWait
            };

            // עד 20 דקות המתנה: רק העוגן
            var shortWait = RoutePlanner.Plan(Input(20));
            Assert.NotNull(shortWait);
            Assert.Equal(new[] { 0 }, shortWait!.Order);
            Assert.Equal(5, shortWait.Value);

            // עד 30 דקות: שתיהן - יציאה ב-+5, איסוף A ב-+15, הגעה ל-B ב-+30, איסוף ב-+60 ועמותה ב-+75
            var longWait = RoutePlanner.Plan(Input(30));
            Assert.NotNull(longWait);
            Assert.Equal(new[] { 0, 1 }, longWait!.Order);
            Assert.Equal(T0.AddMinutes(5), longWait.DepartureUtc);
            Assert.Equal(T0.AddMinutes(60), longWait.ArrivalsUtc[1]);
            Assert.Equal(30, longWait.WaitMinutes, 6);
            Assert.Equal(T0.AddMinutes(75), longWait.NgoArrivalUtc);
            AssertRouteIsFeasible(Input(30), longWait);
        }

        // =====================================================================
        // Branch & Bound גלובלי מול מעבר על כל הצירופים
        // =====================================================================

        private static RouteCandidate Route(int anchor, int volunteer, int ngo, double value, double weightKg, double score, params int[] extras) => new()
        {
            AnchorDonationId = anchor,
            Candidate = new VolunteerCandidate(new Volunteer { Id = volunteer }, 1),
            Ngo = new Ngo { Id = ngo },
            Score = score,
            Plan = new RoutePlan
            {
                Stops = new[] { anchor }.Concat(extras).Select(id => new PickupStop { DonationId = id }).ToList(),
                KnapsackValue = value,
                TotalWeightKg = weightKg,
                TotalDurationMinutes = 30
            }
        };

        private static double BruteForceSelection(List<int> anchors, Dictionary<int, IReadOnlyList<RouteCandidate>> routes, Dictionary<int, double> ngoLeft)
        {
            double best = 0;
            void Walk(int a, double value, HashSet<int> volunteers, HashSet<int> donations, Dictionary<int, double> left)
            {
                if (a == anchors.Count) { best = Math.Max(best, value); return; }
                Walk(a + 1, value, volunteers, donations, left);
                if (!routes.TryGetValue(anchors[a], out var list)) return;

                foreach (var r in list)
                {
                    var ids = r.Plan.Stops.Select(s => s.DonationId).ToList();
                    if (volunteers.Contains(r.Candidate.Volunteer.Id) || ids.Any(donations.Contains)) continue;
                    bool limited = left.TryGetValue(r.Ngo.Id, out var room);
                    if (limited && room + 1e-9 < r.Plan.TotalWeightKg) continue;

                    volunteers.Add(r.Candidate.Volunteer.Id);
                    foreach (var id in ids) donations.Add(id);
                    if (limited) left[r.Ngo.Id] = room - r.Plan.TotalWeightKg;

                    Walk(a + 1, value + r.Value, volunteers, donations, left);

                    volunteers.Remove(r.Candidate.Volunteer.Id);
                    foreach (var id in ids) donations.Remove(id);
                    if (limited) left[r.Ngo.Id] = room;
                }
            }

            Walk(0, 0, new HashSet<int>(), new HashSet<int>(), new Dictionary<int, double>(ngoLeft));
            return best;
        }

        [Fact]
        public void BranchAndBound_FindsTheSameOptimumAsBruteForce_On3000RandomRuns()
        {
            var rnd = new Random(7);
            var search = new BacktrackingAssignmentService();

            for (int iteration = 0; iteration < 3000; iteration++)
            {
                int donations = rnd.Next(1, 8), volunteers = rnd.Next(1, 5);
                var anchors = Enumerable.Range(1, donations).OrderBy(_ => rnd.Next()).ToList();
                var routes = new Dictionary<int, IReadOnlyList<RouteCandidate>>();

                foreach (var anchor in anchors)
                {
                    var list = new List<RouteCandidate>();
                    for (int r = rnd.Next(0, 4); r > 0; r--)
                    {
                        var extras = Enumerable.Range(1, donations + 2).Where(d => d != anchor && rnd.NextDouble() < 0.2).ToArray();
                        list.Add(Route(anchor, rnd.Next(1, volunteers + 1), rnd.Next(1, 3), rnd.Next(1, 20), rnd.Next(1, 15), rnd.Next(0, 101), extras));
                    }
                    routes[anchor] = list;
                }

                var ngoLeft = new Dictionary<int, double> { [1] = rnd.Next(5, 40) };   // עמותה 2 - ללא הגבלה

                var selection = search.SelectRoutes(anchors, routes, ngoLeft);
                double expected = BruteForceSelection(anchors, routes, ngoLeft);

                Assert.True(selection.ProvedOptimal);
                Assert.Equal(expected, selection.TotalValue, 6);
                Assert.True(selection.TotalValue >= selection.GreedyValue - 1e-9);

                // הפתרון עצמו חוקי: מתנדב אחד למסלול, תרומה במסלול אחד לכל היותר, וקיבולת העמותה נשמרת
                Assert.Equal(selection.Routes.Count, selection.Routes.Select(r => r.Candidate.Volunteer.Id).Distinct().Count());
                var ids = selection.Routes.SelectMany(r => r.Plan.Stops.Select(s => s.DonationId)).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                Assert.True(selection.Routes.Where(r => r.Ngo.Id == 1).Sum(r => r.Plan.TotalWeightKg) <= ngoLeft[1] + 1e-9);
                Assert.Equal(selection.TotalValue, selection.Routes.Sum(r => r.Value), 6);
            }
        }

        [Fact]
        public void BranchAndBound_BeatsGreedy_WhenTheBestVolunteerIsTheOnlyOneForAnotherDonation()
        {
            // תרומה 1 (הדחופה): מתנדב 1 (מעט טוב יותר) או מתנדב 2. תרומה 2: רק מתנדב 1.
            var routes = new Dictionary<int, IReadOnlyList<RouteCandidate>>
            {
                [1] = new[] { Route(1, volunteer: 1, ngo: 1, value: 10, weightKg: 5, score: 90), Route(1, volunteer: 2, ngo: 1, value: 10, weightKg: 5, score: 60) },
                [2] = new[] { Route(2, volunteer: 1, ngo: 1, value: 50, weightKg: 40, score: 80) }
            };

            var selection = new BacktrackingAssignmentService().SelectRoutes(new[] { 1, 2 }, routes, new Dictionary<int, double>());

            Assert.True(selection.ProvedOptimal);
            Assert.Equal(2, selection.Routes.Count);
            Assert.True(selection.TotalValue > selection.GreedyValue);
            Assert.Contains(selection.Routes, r => r.AnchorDonationId == 1 && r.Candidate.Volunteer.Id == 2);
            Assert.Contains(selection.Routes, r => r.AnchorDonationId == 2 && r.Candidate.Volunteer.Id == 1);
        }

        [Fact]
        public void BranchAndBound_WithATinyNodeBudget_IsNeverWorseThanGreedy_AndItsUpperBoundIsHonest()
        {
            var rnd = new Random(99);
            var search = new BacktrackingAssignmentService();
            for (int iteration = 0; iteration < 500; iteration++)
            {
                var anchors = Enumerable.Range(1, 7).ToList();
                var routes = anchors.ToDictionary(a => a, a => (IReadOnlyList<RouteCandidate>)Enumerable.Range(0, 3)
                    .Select(_ => Route(a, rnd.Next(1, 4), rnd.Next(1, 3), rnd.Next(1, 20), rnd.Next(1, 15), rnd.Next(0, 101),
                        Enumerable.Range(1, 9).Where(d => d != a && rnd.NextDouble() < 0.25).ToArray()))
                    .ToList());
                var ngoLeft = new Dictionary<int, double> { [1] = rnd.Next(5, 30) };

                var limited = search.SelectRoutes(anchors, routes, ngoLeft, nodeLimit: 2);
                double optimum = BruteForceSelection(anchors, routes, ngoLeft);

                Assert.True(limited.TotalValue >= limited.GreedyValue - 1e-9);   // לעולם לא גרוע מהחמדני
                Assert.True(limited.TotalValue <= optimum + 1e-6);              // פתרון חוקי
                Assert.True(limited.UpperBound >= optimum - 1e-6);              // החסם המדווח באמת חוסם את האופטימום
            }
        }

        [Fact]
        public void Hungarian_MaxWeightMatching_EqualsBruteForce_OnRectangularMatrices()
        {
            var rnd = new Random(17);
            for (int iteration = 0; iteration < 3000; iteration++)
            {
                int rows = rnd.Next(0, 7), cols = rnd.Next(0, 7);
                var w = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        w[r, c] = rnd.NextDouble() < 0.3 ? 0 : rnd.Next(1, 50) / 2.0;   // 0 = אין זוג

                double best = 0;
                void Walk(int r, bool[] usedCols, double value)
                {
                    if (r == rows) { best = Math.Max(best, value); return; }
                    Walk(r + 1, usedCols, value);                                  // השורה לא מותאמת
                    for (int c = 0; c < cols; c++)
                    {
                        if (usedCols[c] || w[r, c] <= 0) continue;
                        usedCols[c] = true;
                        Walk(r + 1, usedCols, value + w[r, c]);
                        usedCols[c] = false;
                    }
                }
                Walk(0, new bool[cols], 0);

                var (value, assignment) = HungarianAlgorithm.MaxWeightMatching(w);

                Assert.Equal(best, value, 6);
                var usedColumns = assignment.Where(c => c >= 0).ToList();
                Assert.Equal(usedColumns.Count, usedColumns.Distinct().Count());   // כל עמודה פעם אחת לכל היותר
                Assert.Equal(value, assignment.Select((c, r) => c >= 0 ? w[r, c] : 0).Sum(), 6);
            }
        }

        // =====================================================================
        // KD-Tree (Divide & Conquer גיאוגרפי) מול סריקה של כל הנקודות
        // =====================================================================

        [Fact]
        public void KdTree_RadiusSearch_EqualsALinearScan()
        {
            var rnd = new Random(31);
            for (int iteration = 0; iteration < 200; iteration++)
            {
                var points = Enumerable.Range(0, rnd.Next(0, 300))
                    .Select(i => (Id: i, Lat: 29.5 + rnd.NextDouble() * 3.8, Lon: 34.2 + rnd.NextDouble() * 1.7))
                    .ToList();
                var tree = new GeoKdTree<(int Id, double Lat, double Lon)>(points, p => p.Lat, p => p.Lon);

                for (int q = 0; q < 20; q++)
                {
                    double lat = 29.5 + rnd.NextDouble() * 3.8, lon = 34.2 + rnd.NextDouble() * 1.7, radius = rnd.NextDouble() * 60;

                    var expected = points.Where(p => GeoUtils.HaversineKm(lat, lon, p.Lat, p.Lon) <= radius).Select(p => p.Id).OrderBy(x => x);
                    var actual = tree.WithinRadius(lat, lon, radius).Select(p => p.Id).OrderBy(x => x);

                    Assert.Equal(expected, actual);
                }
            }
        }

        [Fact]
        public void KdTree_HandlesTheDateLine_AndDuplicates()
        {
            var points = new[] { (Id: 1, Lat: 0.0, Lon: 179.95), (Id: 2, Lat: 0.0, Lon: -179.95), (Id: 3, Lat: 0.0, Lon: 179.95), (Id: 4, Lat: 0.0, Lon: 170.0) };
            var tree = new GeoKdTree<(int Id, double Lat, double Lon)>(points, p => p.Lat, p => p.Lon);

            var found = tree.WithinRadius(0, 180, 20).Select(p => p.Id).OrderBy(x => x).ToArray();

            Assert.Equal(new[] { 1, 2, 3 }, found);
            Assert.Empty(new GeoKdTree<int>(Array.Empty<int>(), _ => 0, _ => 0).WithinRadius(0, 0, 100));
        }

        // =====================================================================
        // Knapsack (DP עם מגבלת קטגוריה ותרומת עוגן) מול מעבר על כל תתי-הקבוצות
        // =====================================================================

        [Fact]
        public void Knapsack_FindsTheSameOptimumAsBruteForce_On3000RandomCases()
        {
            var rnd = new Random(5);
            var solver = new KnapsackSolver();

            for (int iteration = 0; iteration < 3000; iteration++)
            {
                int n = rnd.Next(1, 11);
                var items = Enumerable.Range(1, n)
                    .Select(i => new KnapsackItem(new Donation { Id = i }, rnd.Next(1, 25), rnd.Next(1, 100) / 10.0, rnd.Next(1, 4)))
                    .ToList();
                int capacity = rnd.Next(0, 80);
                int perCategory = rnd.Next(0, 4);                     // 0 = ללא הגבלה
                int? anchor = rnd.NextDouble() < 0.5 ? rnd.Next(1, n + 1) : null;

                double expected = 0;
                for (int mask = 0; mask < (1 << n); mask++)
                {
                    var set = items.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                    if (anchor.HasValue && set.All(x => x.Donation.Id != anchor.Value)) continue;
                    if (set.Sum(x => x.WeightUnits) > capacity) continue;
                    if (perCategory > 0 && set.GroupBy(x => x.CategoryId).Any(g => g.Count() > perCategory)) continue;
                    expected = Math.Max(expected, set.Sum(x => x.Value));
                }

                var result = solver.Solve(items, capacity, perCategory, anchor);

                Assert.Equal(expected, result.TotalValue, 6);
                Assert.True(result.TotalUnits <= capacity);
                Assert.Equal(expected, items.Where(i => result.SelectedDonations.Contains(i.Donation)).Sum(i => i.Value), 6);
            }
        }

        // =====================================================================
        // Max-Heap
        // =====================================================================

        [Fact]
        public void MaxHeap_ExtractsByPriority_ThenByTheTieBreaker_AfterUpdatesAndRemovals()
        {
            var rnd = new Random(3);
            for (int iteration = 0; iteration < 200; iteration++)
            {
                var donations = Enumerable.Range(1, rnd.Next(1, 60)).Select(i => new Donation { Id = i }).ToList();
                var priority = donations.ToDictionary(d => d.Id, _ => (double)rnd.Next(0, 10));   // הרבה שוויונות

                var heap = new PriorityHeapService<Donation, int>(d => d.Id, (a, b) => a.Id.CompareTo(b.Id));
                heap.LoadOrUpdateAll(donations.Select(d => (d, priority[d.Id])));

                foreach (var d in donations.Where(_ => rnd.NextDouble() < 0.3).ToList())
                {
                    priority[d.Id] = rnd.Next(0, 10);
                    heap.UpdatePriority(d, priority[d.Id]);
                }
                foreach (var d in donations.Where(_ => rnd.NextDouble() < 0.2).ToList())
                {
                    heap.Remove(d);
                    priority.Remove(d.Id);
                }

                var expected = priority.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToList();
                var actual = new List<int>();
                while (heap.Count > 0) actual.Add(heap.ExtractMax()!.Id);

                Assert.Equal(expected, actual);
            }
        }

        // =====================================================================
        // Merge Sort / Quick Sort
        // =====================================================================

        [Fact]
        public void MergeSort_IsStable_AndQuickSort_SortsListsFullOfDuplicates()
        {
            var rnd = new Random(11);
            for (int iteration = 0; iteration < 300; iteration++)
            {
                var items = Enumerable.Range(0, rnd.Next(0, 200)).Select(i => (Key: rnd.Next(0, 5), Index: i)).ToList();
                Comparison<(int Key, int Index)> byKey = (a, b) => a.Key.CompareTo(b.Key);

                var merged = CustomSortAlgorithms<(int Key, int Index)>.MergeSort(items, byKey);
                Assert.Equal(items.OrderBy(x => x.Key).ToList(), merged);   // OrderBy יציב - כולל סדר השווים

                var quick = new List<(int Key, int Index)>(items);
                CustomSortAlgorithms<(int Key, int Index)>.QuickSort(quick, byKey);
                Assert.Equal(items.Select(x => x.Key).OrderBy(x => x), quick.Select(x => x.Key));
                Assert.Equal(items.OrderBy(x => x.Index), quick.OrderBy(x => x.Index));
            }

            // 100,000 ערכים שווים: החלוקה לשלושה מסיימת מיד (בעבר - O(n²))
            var same = Enumerable.Repeat(7, 100_000).ToList();
            CustomSortAlgorithms<int>.QuickSort(same, (a, b) => a.CompareTo(b));
            Assert.All(same, x => Assert.Equal(7, x));
        }

        // =====================================================================
        // חלונות זמינות
        // =====================================================================

        [Fact]
        public void AvailabilityWindows_OvernightSlot_CoversARouteThatCrossesMidnight()
        {
            LocalClock.Configure("Asia/Jerusalem");
            var volunteer = new Volunteer
            {
                AvailabilitySlots = new List<AvailabilitySlot>
                {
                    new() { RegionId = 1, Day = DayOfWeek.Monday, StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(2, 0, 0) }
                }
            };
            DateTime Local(int day, int hour, int minute) => LocalClock.ToUtc(new DateTime(2026, 10, day, hour, minute, 0));

            Assert.True(AvailabilityHelper.CoversRoute(volunteer, Local(19, 23, 30), Local(20, 0, 40), new[] { 1 }, 1));
            Assert.False(AvailabilityHelper.CoversRoute(volunteer, Local(19, 23, 30), Local(20, 2, 30), new[] { 1 }, 1));
            Assert.False(AvailabilityHelper.CoversRoute(volunteer, Local(19, 21, 30), Local(19, 22, 30), new[] { 1 }, 1));

            var windows = AvailabilityHelper.GetWindows(volunteer, Local(19, 0, 0), Local(26, 23, 0));
            Assert.Equal(2, windows.Count);                                     // שני ימי שני בטווח
            Assert.Equal(Local(19, 22, 0), windows[0].StartUtc);
            Assert.Equal(Local(20, 2, 0), windows[0].EndUtc);
        }

        // =====================================================================
        // קיבולת העמותה בק"ג
        // =====================================================================

        [Fact]
        public async Task NgoLoad_CountsKilograms_OfLiveAndCompletedMatchesOnly()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");

            async Task AddMatchAsync(double kg, MatchStatus status)
            {
                var d = await h.AddDonationAsync(weightKg: kg);
                h.Db.Matches.Add(new Match
                {
                    DonationId = d.Id, NgoId = h.Ngo.Id, VolunteerId = v.Id, Status = status,
                    IsActive = status != MatchStatus.Declined && status != MatchStatus.TimedOut, AssignedAt = h.NowUtc
                });
                await h.Db.SaveChangesAsync();
            }

            await AddMatchAsync(8, MatchStatus.AwaitingVolunteerConfirmation);
            await AddMatchAsync(5, MatchStatus.Completed);
            await AddMatchAsync(30, MatchStatus.Declined);
            await AddMatchAsync(30, MatchStatus.TimedOut);
            await AddMatchAsync(30, MatchStatus.NoShow);
            await AddMatchAsync(30, MatchStatus.Cancelled);

            var service = new NgoSelectionService(h.Matches, h.Policy, NullLogger<NgoSelectionService>.Instance);
            h.Ngo.DailyCapacity = 20;
            var remaining = await service.GetRemainingCapacityKgAsync(new[] { h.Ngo }, h.NowUtc);

            Assert.Equal(20 - 8 - 5, remaining[h.Ngo.Id], 6);
        }

        [Fact]
        public void RankNgos_SkipsFullOrClosedNgos_AndPutsTheEarliestArrivalFirst()
        {
            LocalClock.Configure("Asia/Jerusalem");
            var now = LocalClock.ToUtc(new DateTime(2026, 10, 19, 10, 0, 0));   // יום שני
            var donation = new Donation
            {
                Id = 1, Latitude = 32.08, Longitude = 34.78, CreatedAt = now, LatestPickupTime = new TimeSpan(13, 0, 0),
                ExpirationTime = now.AddHours(4),
                DonationFoodCategories = new List<DonationFoodCategory> { new() { CategoryId = 1, WeightKg = 10 } }
            };
            Ngo Ngo(int id, double lat, int capacity, int opensAt = 0) => new()
            {
                Id = id, Latitude = lat, Longitude = 34.78, DailyCapacity = capacity,
                SunToThuOpenTime = TimeSpan.FromHours(opensAt), SunToThuCloseTime = new TimeSpan(23, 0, 0)
            };

            var near = Ngo(1, 32.09, 100);
            var far = Ngo(2, 32.20, 100);
            var full = Ngo(3, 32.081, 15);                  // נשארו לה 5 ק"ג - התרומה 10 ק"ג
            var opensLate = Ngo(4, 32.082, 100, opensAt: 15);   // נפתחת אחרי התפוגה
            var unlimited = Ngo(5, 32.15, 0);               // 0 = ללא הגבלה

            var service = new NgoSelectionService(null!, new StaticMatchingPolicyProvider(), NullLogger<NgoSelectionService>.Instance);
            var ranked = service.RankNgos(donation, new[] { far, full, near, opensLate, unlimited },
                new Dictionary<int, double> { [1] = 100, [2] = 100, [3] = 5, [4] = 100 }, now);

            Assert.Equal(new[] { 1, 5, 2 }, ranked.Select(o => o.Ngo.Id).ToArray());
        }
    }
}
