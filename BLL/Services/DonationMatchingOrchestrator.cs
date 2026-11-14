using BLL.Services;
using BLL.Configuration;
using DAL.Classes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    // מסלול מוצע למתנדב: כמה תורמים -> עמותה אחת
    public sealed class RoutePlan
    {
        public List<PickupStop> Stops { get; init; } = new();
        public GeoCoordinate StartLocation { get; init; } = new();

        /// <summary>שעת היציאה המתוכננת (אחרי חלון התגובה של המתנדב; יכולה להיות מאוחרת יותר - תיקונים 24, 26).</summary>
        public DateTime StartTime { get; init; }
        public DateTime NgoArrival { get; init; }
        public double NgoLegDistanceKm { get; init; }
        public double NgoLegDurationMinutes { get; init; }
        public double TotalDistanceKm { get; init; }
        public double TotalDurationMinutes { get; init; }
        public double TotalWeightKg { get; init; }
        public double KnapsackValue { get; init; }

        /// <summary>true = קבוצת התרומות היא האופטימום של ה-Knapsack עצמו (חסם עליון) - ולכן אין מסלול בעל ערך גבוה יותר.</summary>
        public bool ProvenOptimal { get; init; }
    }

    /// <summary>בקשה לתכנון מסלול אחד: מתנדב + חלון זמינות + תרומת עוגן + עמותה.</summary>
    public sealed class RouteRequest
    {
        public Volunteer Volunteer { get; init; } = null!;
        public Donation Anchor { get; init; } = null!;

        /// <summary>תרומות נוספות אפשריות (בדרך כלל - בטווח האשכול של העוגן).</summary>
        public IReadOnlyList<Donation> Pool { get; init; } = Array.Empty<Donation>();

        public Ngo Ngo { get; init; } = null!;
        public AvailabilityWindow Window { get; init; }
        public GeoCoordinate Start { get; init; } = new();

        /// <summary>מתי נשלחת ההצעה (עכשיו). ממנו מחושבת הדחיפות, וממנו נבדקת השבת.</summary>
        public DateTime OfferTimeUtc { get; init; }
        public DateTime EarliestDepartureUtc { get; init; }
        public DateTime LatestDepartureUtc { get; init; }

        /// <summary>הקיבולת שנשארה היום לעמותה (ק"ג). אינסוף = ללא הגבלה.</summary>
        public double NgoRemainingKg { get; init; } = double.PositiveInfinity;
    }

    /// <summary>
    /// בונה את המסלול הטוב ביותר למתנדב, לתרומת עוגן ולעמותה נתונים.
    ///
    ///   1. Knapsack 0/1 (KnapsackSolver): אילו תרומות נוספות שוות לאסוף - בקיבולת הרכב (וגם בקיבולת שנשארה לעמותה,
    ///      תיקון 21) ובמגבלת המגוון לקטגוריה.
    ///   2. RoutePlanner (DP מדויק): מתוך התרומות שנבחרו - איזו תת-קבוצה ובאיזה סדר עומדת בכל אילוצי הזמן
    ///      (מועדי איסוף, "זמינה החל מ-", תפוגה, שעות העמותה, זמינות המתנדב, שבת), ומתי לצאת.
    ///   3. אם ה-Knapsack "מילא" תרומות שלא נכנסות בזמן - סבב שני: ה-Knapsack ממלא את המקום שהתפנה בתרומות אחרות,
    ///      ונבחר המסלול הטוב מבין השניים (תיקון 22: בעבר כל המסלול נפסל במקרה כזה).
    ///
    /// הוכחת אופטימליות (ProvenOptimal): ה-Knapsack פותר בעיה "קלה" יותר - בלי אילוצי זמן - ולכן הערך שלו הוא חסם
    /// עליון לכל מסלול. אם כל מה שהוא בחר נכנס בזמן, אין מסלול טוב יותר מהתרומות האלה.
    /// </summary>
    public class DonationMatchingOrchestrator
    {
        private readonly KnapsackSolver _knapsackSolver;
        private readonly IRouteOptimizationService _routeOptimizationService;
        private readonly IMatchingPolicyProvider _policy;
        private readonly ILogger<DonationMatchingOrchestrator> _logger;

        public DonationMatchingOrchestrator(
            KnapsackSolver knapsackSolver,
            IRouteOptimizationService routeOptimizationService,
            IMatchingPolicyProvider policy,
            ILogger<DonationMatchingOrchestrator> logger)
        {
            _knapsackSolver = knapsackSolver;
            _routeOptimizationService = routeOptimizationService;
            _policy = policy;
            _logger = logger;
        }

        internal bool IsWithinPickupDeadline(PickupStop stop, Donation donation)
        {
            if (stop == null || donation == null) return false;
            if (!stop.EstimatedArrival.HasValue) return false;
            var pickupDateTime = NgoAvailabilityHelper.GetPickupDateTime(donation);
            return stop.EstimatedArrival.Value <= pickupDateTime;
        }

        internal bool VolunteerAvailabilityCoversRoute(Volunteer volunteer, DateTime routeStart, DateTime routeEnd, List<Donation> routeDonations, Ngo targetNgo)
        {
            if (routeDonations == null || routeDonations.Count == 0) return false;

            var donationRegionIds = routeDonations.Select(d => d.RegionId).Distinct().ToList();
            return AvailabilityHelper.CoversRoute(volunteer, routeStart, routeEnd, donationRegionIds, targetNgo.RegionId);
        }

        // תאימות: מחזיר רק את התחנות (רשימה ריקה = אין מסלול אפשרי)
        public async Task<List<PickupStop>> ProcessVolunteerRouteAssignmentAsync(
            Volunteer volunteer,
            List<Donation> availableDonations,
            GeoCoordinate volunteerStartLocation,
            Ngo targetNgo,
            DateTime? startTime = null,
            int? anchorDonationId = null)
        {
            var plan = await BuildRouteAsync(volunteer, availableDonations, volunteerStartLocation, targetNgo, startTime, anchorDonationId);
            return plan?.Stops ?? new List<PickupStop>();
        }

        /// <summary>
        /// תאימות: מסלול שיוצא בדיוק ב-startTime (בלי דחייה) עם משבצת הזמינות של המתנדב שפתוחה באותו רגע.
        /// בלי תרומת עוגן - נבדקות התרומות הדחופות ביותר בטווח החיפוש כעוגן, ונבחר המסלול הטוב.
        /// </summary>
        public async Task<RoutePlan?> BuildRouteAsync(
            Volunteer volunteer,
            List<Donation> availableDonations,
            GeoCoordinate volunteerStartLocation,
            Ngo targetNgo,
            DateTime? startTime = null,
            int? anchorDonationId = null)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));

            var policy = _policy.Current;
            var start = volunteerStartLocation ?? new GeoCoordinate();
            var departure = startTime ?? DateTime.UtcNow;
            var pending = (availableDonations ?? new List<Donation>())
                .Where(d => d.Status == DonationStatus.Pending)
                .ToList();

            var anchors = anchorDonationId.HasValue
                ? pending.Where(d => d.Id == anchorDonationId.Value).ToList()
                : pending
                    .Where(d => GeoUtils.HaversineKm(start.Latitude, start.Longitude, d.Latitude, d.Longitude) <= policy.MaxSearchRadiusKm)
                    .OrderByDescending(d => PriorityCalculator.Calculate(d, departure, policy.UrgencyTimeConstantMinutes))
                    .ThenBy(d => d.Id)
                    .Take(5)
                    .ToList();

            RoutePlan? best = null;
            foreach (var anchor in anchors)
            {
                var window = AvailabilityHelper.GetWindows(volunteer, departure, departure)
                    .Where(w => w.Contains(departure) && AvailabilityHelper.SlotServesRoute(w.Slot, new[] { anchor.RegionId }, targetNgo.RegionId))
                    .OrderByDescending(w => w.EndUtc)
                    .FirstOrDefault();
                if (window.Slot == null)
                    continue;

                var pool = pending
                    .Where(d => d.Id != anchor.Id &&
                                GeoUtils.HaversineKm(anchor.Latitude, anchor.Longitude, d.Latitude, d.Longitude) <= policy.ClusterRadiusKm)
                    .ToList();

                var plan = await PlanAsync(new RouteRequest
                {
                    Volunteer = volunteer,
                    Anchor = anchor,
                    Pool = pool,
                    Ngo = targetNgo,
                    Window = window,
                    Start = start,
                    OfferTimeUtc = departure,
                    EarliestDepartureUtc = departure,
                    LatestDepartureUtc = departure
                });

                if (IsBetter(plan, best))
                    best = plan;
            }

            return best;
        }

        /// <summary>המסלול הטוב ביותר לבקשה (null = אין מסלול שעומד בכל האילוצים).</summary>
        public async Task<RoutePlan?> PlanAsync(RouteRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Volunteer == null || request.Anchor == null || request.Ngo == null || request.Window.Slot == null)
                throw new ArgumentException("בקשת המסלול חסרה מתנדב, תרומה, עמותה או חלון זמינות.", nameof(request));

            var policy = _policy.Current;
            var anchor = request.Anchor;
            var slot = request.Window.Slot;

            var earliest = Max(request.EarliestDepartureUtc, request.Window.StartUtc);
            var latest = Min(request.LatestDepartureUtc, request.Window.EndUtc);
            if (earliest > latest)
                return null;

            if (anchor.Status != DonationStatus.Pending || anchor.TotalWeightKg() <= 0 ||
                NgoAvailabilityHelper.GetPickupDateTime(anchor) < earliest || anchor.ExpirationTime <= earliest)
                return null;

            // קיבולת: הרכב, וגם מה שנשאר היום לעמותה (תיקון 21)
            double capacityKg = Math.Min(request.Volunteer.MaxCapacityKg, request.NgoRemainingKg);
            if (anchor.TotalWeightKg() > capacityKg + 1e-9)
                return null;

            // תרומות נוספות אפשריות: ממתינות, עם זמן לאיסוף, ובאזורים שהמשבצת של המתנדב משרתת לעמותה הזו
            var extras = (request.Pool ?? Array.Empty<Donation>())
                .Where(d => d.Id != anchor.Id &&
                            d.Status == DonationStatus.Pending &&
                            d.TotalWeightKg() > 0 &&
                            NgoAvailabilityHelper.GetPickupDateTime(d) >= earliest &&
                            d.ExpirationTime > earliest &&
                            AvailabilityHelper.SlotServesRoute(slot, new[] { anchor.RegionId, d.RegionId }, request.Ngo.RegionId))
                .GroupBy(d => d.Id)
                .Select(g => g.First())
                .ToList();

            var items = KnapsackItem.CreateItems(
                new[] { anchor }.Concat(extras),
                anchor.Latitude, anchor.Longitude,
                policy.ClusterRadiusKm,
                policy.DistancePenaltyCoefficient,
                request.OfferTimeUtc,
                policy.UnitSizeGrams,
                policy.UrgencyTimeConstantMinutes,
                anchor.Id);

            int unitGrams = Math.Max(1, policy.UnitSizeGrams);
            int capacityUnits = (int)Math.Min(1_000_000_000d, Math.Floor(capacityKg * 1000.0 / unitGrams));

            // סבב 1: ה-Knapsack בוחר, ה-DP בודק זמנים ומסדר
            var first = _knapsackSolver.Solve(items, capacityUnits, policy.MaxItemsPerCategory, anchor.Id);
            if (first.SelectedDonations.Count == 0)
                return null;

            var plan = await PlanExactAsync(first.SelectedDonations, items, request, earliest, latest, capacityKg);
            if (plan != null && plan.Stops.Count == first.SelectedDonations.Count)
                return WithProvenOptimal(plan);

            // סבב 2: בלי התרומות שלא נכנסו בזמן - ה-Knapsack ממלא את הקיבולת שהתפנתה בתרומות אחרות
            var dropped = first.SelectedDonations
                .Where(d => d.Id != anchor.Id && (plan == null || plan.Stops.All(s => s.DonationId != d.Id)))
                .Select(d => d.Id)
                .ToHashSet();
            if (dropped.Count == 0)
                return plan;

            var second = _knapsackSolver.Solve(items.Where(i => !dropped.Contains(i.Donation.Id)).ToList(),
                capacityUnits, policy.MaxItemsPerCategory, anchor.Id);
            var plan2 = second.SelectedDonations.Count == 0
                ? null
                : await PlanExactAsync(second.SelectedDonations, items, request, earliest, latest, capacityKg);

            return IsBetter(plan2, plan) ? plan2 : plan;
        }

        /// <summary>
        /// תיקון 66: למה אין מסלול לתרומה אחת (שיבוץ ידני של המנהל) - הסיבה המדויקת, עם השעות.
        /// עובר על אותם אילוצים כמו PlanAsync + RoutePlanner, באותו סדר, למסלול של תחנה אחת:
        /// משמרת המתנדב, תפוגה, שעת האיסוף האחרונה, קיבולת העמותה, ההגעה לאיסוף, ההגעה לעמותה, שעות הקבלה והשבת.
        /// null = לא נמצאה סיבה אחת ברורה (המקרה הכללי - ההודעה הכללית נשארת).
        /// </summary>
        public async Task<string?> ExplainNoRouteAsync(RouteRequest request)
        {
            if (request?.Anchor == null || request.Ngo == null || request.Window.Slot == null)
                return null;

            var policy = _policy.Current;
            var anchor = request.Anchor;
            var ngo = request.Ngo;
            var now = request.OfferTimeUtc;
            string When(DateTime utc) => FormatWhen(utc, now);

            var deadline = NgoAvailabilityHelper.GetPickupDateTime(anchor);
            var earliest = Max(request.EarliestDepartureUtc, request.Window.StartUtc);
            var latest = Min(request.LatestDepartureUtc, request.Window.EndUtc);

            if (earliest > latest)
                return $"משמרת הזמינות של המתנדב מסתיימת ב-{When(request.Window.EndUtc)}, לפני שאפשר לצאת לדרך.";
            if (anchor.ExpirationTime <= earliest)
                return $"המזון פג תוקף ב-{When(anchor.ExpirationTime)}, והמתנדב יכול לצאת לכל המוקדם ב-{When(earliest)}.";
            if (deadline < earliest)
                return $"שעת האיסוף האחרונה ({When(deadline)}) עוברת לפני שהמתנדב יכול לצאת ({When(earliest)}" +
                       (earliest == request.EarliestDepartureUtc ? $", אחרי {policy.ResponseTimeoutMinutes} דקות לתגובה)." : ").");

            double weightKg = anchor.TotalWeightKg();
            if (weightKg > request.Volunteer.MaxCapacityKg + 1e-9)
                return $"קיבולת הרכב של המתנדב ({request.Volunteer.MaxCapacityKg:0.#} ק\"ג) קטנה ממשקל התרומה ({weightKg:0.#} ק\"ג).";
            if (weightKg > request.NgoRemainingKg + 1e-9)
                return $"לעמותה נשארו היום רק {Math.Max(0, request.NgoRemainingKg):0.#} ק\"ג מהקיבולת היומית, והתרומה שוקלת {weightKg:0.#} ק\"ג.";

            // זמני הנסיעה - מאותו שירות כמו המתכנן (Google עם מטמון, ואם אין - הערכה)
            var donationPoint = new GeoCoordinate { Latitude = anchor.Latitude, Longitude = anchor.Longitude };
            var ngoPoint = new GeoCoordinate { Latitude = ngo.Latitude, Longitude = ngo.Longitude };
            var (_, toDonation) = await _routeOptimizationService.EstimateLegAsync(request.Start, donationPoint);
            var (_, toNgo) = await _routeOptimizationService.EstimateLegAsync(donationPoint, ngoPoint);
            toDonation = Math.Max(0, toDonation);
            toNgo = Math.Max(0, toNgo);
            double service = Math.Max(0, policy.StopServiceMinutes);

            // בלי לדחות את היציאה: מתי מגיעים לאיסוף, ומתי אפשר להתחיל לאסוף ("זמינה החל מ-")
            var reach = earliest.AddMinutes(toDonation);
            DateTime? ready = anchor.EarliestPickupTime.HasValue ? NgoAvailabilityHelper.GetEarliestPickupDateTime(anchor) : null;
            var pickup = ready.HasValue && ready.Value > reach ? ready.Value : reach;
            if (pickup > deadline)
                return ready.HasValue && ready.Value > reach
                    ? $"התרומה זמינה לאיסוף רק מ-{When(ready.Value)}, אחרי שעת האיסוף האחרונה ({When(deadline)})."
                    : $"המתנדב יגיע לנקודת האיסוף לכל המוקדם ב-{When(reach)} ({toDonation:0} דקות נסיעה), אחרי שעת האיסוף האחרונה ({When(deadline)}).";

            var arrivalNoDelay = pickup.AddMinutes(service + toNgo);
            if (arrivalNoDelay > anchor.ExpirationTime)
                return $"ההגעה לעמותה תהיה לכל המוקדם ב-{When(arrivalNoDelay)}, אחרי תפוגת המזון ({When(anchor.ExpirationTime)}).";
            if (arrivalNoDelay > request.Window.EndUtc)
                return $"ההגעה לעמותה תהיה לכל המוקדם ב-{When(arrivalNoDelay)}, אחרי סוף משמרת הזמינות של המתנדב ({When(request.Window.EndUtc)}).";

            // כמו RoutePlanner.TryTiming: עד כמה אפשר לדחות את היציאה (מועד האיסוף, אופק התכנון, תפוגה וסוף המשמרת)
            double T = toDonation;
            double F = (pickup - earliest).TotalMinutes;
            double tNgo = T + service + toNgo;
            double fNgo = F + service + toNgo;
            var latestNgoArrival = Min(anchor.ExpirationTime, request.Window.EndUtc);
            double maxDelay = Math.Min((deadline - earliest).TotalMinutes - T,
                              Math.Min((latest - earliest).TotalMinutes, (latestNgoArrival - earliest).TotalMinutes - tNgo));
            double maxWait = Math.Max(0, policy.MaxPickupWaitMinutes);
            double minDelay = Math.Max(0, (F - T) - maxWait);
            if (maxDelay < minDelay - 1e-9)
                return $"התרומה זמינה לאיסוף רק מ-{When(ready ?? pickup)}, והמתנדב היה צריך לחכות שם יותר מ-{maxWait} דקות.";

            var fromArrival = earliest.AddMinutes(Math.Max(minDelay + tNgo, fNgo));
            var toArrival = earliest.AddMinutes(Math.Max(Math.Max(maxDelay, minDelay) + tNgo, fNgo));
            var open = NgoAvailabilityHelper.EarliestOpenAtOrAfter(ngo, fromArrival, toArrival);
            if (!open.HasValue)
            {
                var range = (toArrival - fromArrival).TotalMinutes < 1
                    ? When(fromArrival)
                    : $"{When(fromArrival)}–{When(toArrival)}";
                var next = NgoAvailabilityHelper.EarliestOpenAtOrAfter(ngo, fromArrival, fromArrival.AddDays(7));
                return $"העמותה סגורה בשעות שבהן המתנדב יכול להגיע אליה ({range})" +
                       (next.HasValue ? $" - היא נפתחת שוב ב-{When(next.Value)}" : " - אין לה שעות קבלה בשבוע הקרוב") +
                       $", והתרומה צריכה להיאסף עד {When(deadline)}.";
            }

            var arrival = open.Value > fromArrival ? open.Value : fromArrival;
            if (ShabbatClock.Overlaps(now, arrival, policy))
            {
                var shabbat = ShabbatClock.CurrentOrNext(now, policy);
                return now >= shabbat.StartUtc
                    ? $"עכשיו שבת - לא משבצים עד צאת השבת ({When(shabbat.EndUtc)})."
                    : $"המסלול נכנס לשבת: ההגעה לעמותה תהיה ב-{When(arrival)}, והשבת נכנסת ב-{When(shabbat.StartUtc)}.";
            }

            return null;
        }

        private static readonly string[] HebrewDays = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };

        // תיקון 66: שעה מקומית; ביום אחר - עם היום ("00:05 (מחר)", "08:00 (יום ראשון)")
        private static string FormatWhen(DateTime utc, DateTime nowUtc)
        {
            var local = LocalClock.ToLocal(utc);
            var today = LocalClock.ToLocal(nowUtc).Date;
            var time = local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (local.Date == today) return time;
            if (local.Date == today.AddDays(1)) return $"{time} (מחר)";
            return $"{time} (יום {HebrewDays[(int)local.DayOfWeek]})";
        }

        // מסלול א' עדיף על ב': ערך גבוה יותר; בשוויון - מגיע לעמותה מוקדם יותר
        public static bool IsBetter(RoutePlan? candidate, RoutePlan? current)
        {
            if (candidate == null) return false;
            if (current == null) return true;
            if (candidate.KnapsackValue > current.KnapsackValue + 1e-9) return true;
            return Math.Abs(candidate.KnapsackValue - current.KnapsackValue) <= 1e-9 && candidate.NgoArrival < current.NgoArrival;
        }

        private async Task<RoutePlan?> PlanExactAsync(
            IReadOnlyList<Donation> selected,
            IReadOnlyList<KnapsackItem> items,
            RouteRequest request,
            DateTime earliestDeparture,
            DateTime latestDeparture,
            double capacityKg)
        {
            var policy = _policy.Current;
            var valueById = items.GroupBy(i => i.Donation.Id).ToDictionary(g => g.Key, g => g.First().Value);

            // העוגן ראשון, אחריו לפי ערך. ה-DP מדויק עד MaxExactStops תחנות - מעבר לזה נשארות התרומות בעלות הערך הגבוה.
            var stops = selected
                .Where(d => d.Id != request.Anchor.Id)
                .OrderByDescending(d => valueById.TryGetValue(d.Id, out var v) ? v : 0)
                .ThenBy(d => d.Id)
                .Take(RoutePlanner.MaxExactStops - 1)
                .Prepend(request.Anchor)
                .ToList();

            // נקודות: 0 = יציאה, 1..m = התחנות, m+1 = העמותה. זמני הנסיעה מ-Google (עם מטמון), ואם אין - הערכה.
            var points = new List<GeoCoordinate> { request.Start };
            points.AddRange(stops.Select(d => new GeoCoordinate { Latitude = d.Latitude, Longitude = d.Longitude }));
            points.Add(new GeoCoordinate { Latitude = request.Ngo.Latitude, Longitude = request.Ngo.Longitude });

            int size = points.Count;
            var minutes = new double[size, size];
            var km = new double[size, size];
            for (int i = 0; i < size - 1; i++)                 // מהעמותה לא יוצאים
            {
                for (int j = 1; j < size; j++)                 // לנקודת היציאה לא חוזרים
                {
                    if (i == j || (i == 0 && j == size - 1)) continue;   // ישר מהיציאה לעמותה - לא קיים (יש לפחות תחנה אחת)
                    var (legKm, legMinutes) = await _routeOptimizationService.EstimateLegAsync(points[i], points[j]);
                    km[i, j] = Math.Max(0, legKm);
                    minutes[i, j] = Math.Max(0, legMinutes);
                }
            }

            var ngo = request.Ngo;
            var result = RoutePlanner.Plan(new PlannerInput
            {
                Stops = stops.Select(d => new PlannerStop
                {
                    DonationId = d.Id,
                    PickupDeadlineUtc = NgoAvailabilityHelper.GetPickupDateTime(d),
                    // תיקון 43: "זמינה לאיסוף החל מ-" - המתכנן לא מביא את המתנדב לפני השעה הזו
                    EarliestPickupUtc = d.EarliestPickupTime.HasValue ? NgoAvailabilityHelper.GetEarliestPickupDateTime(d) : null,
                    ExpirationUtc = d.ExpirationTime,
                    Value = valueById.TryGetValue(d.Id, out var v) ? v : 0,
                    WeightKg = d.TotalWeightKg()
                }).ToList(),
                AnchorIndex = 0,
                Minutes = minutes,
                Km = km,
                EarliestDepartureUtc = earliestDeparture,
                LatestDepartureUtc = latestDeparture,
                AvailableUntilUtc = request.Window.EndUtc,
                ServiceMinutes = policy.StopServiceMinutes,
                CapacityKg = capacityKg,
                MaxWaitMinutes = Math.Max(0, policy.MaxPickupWaitMinutes),
                EarliestNgoOpen = (from, to) => NgoAvailabilityHelper.EarliestOpenAtOrAfter(ngo, from, to),
                // תיקון 9: כל המסלול - מההצעה ועד המסירה בעמותה - לפני כניסת השבת או אחרי צאתה
                IsArrivalBlocked = arrival => ShabbatClock.Overlaps(request.OfferTimeUtc, arrival, policy)
            });

            if (result == null)
            {
                _logger.LogDebug("Planner: no feasible route for volunteer {VolunteerId}, anchor {DonationId}, NGO {NgoId}.",
                    request.Volunteer.Id, request.Anchor.Id, ngo.Id);
                return null;
            }

            var routeStops = new List<PickupStop>(result.Order.Count);
            int from = 0;
            for (int k = 0; k < result.Order.Count; k++)
            {
                int index = result.Order[k];
                var donation = stops[index];
                routeStops.Add(new PickupStop
                {
                    DonationId = donation.Id,
                    Location = points[index + 1],
                    EstimatedArrival = result.ArrivalsUtc[k],
                    LegDistanceKm = km[from, index + 1],
                    LegDurationMinutes = minutes[from, index + 1]
                });
                from = index + 1;
            }

            return new RoutePlan
            {
                Stops = routeStops,
                StartLocation = request.Start,
                StartTime = result.DepartureUtc,
                NgoArrival = result.NgoArrivalUtc,
                NgoLegDistanceKm = km[from, size - 1],
                NgoLegDurationMinutes = minutes[from, size - 1],
                TotalDistanceKm = result.DrivingKm,
                TotalDurationMinutes = (result.NgoArrivalUtc - result.DepartureUtc).TotalMinutes,
                TotalWeightKg = result.WeightKg,
                KnapsackValue = result.Value
            };
        }

        private static RoutePlan WithProvenOptimal(RoutePlan plan) => new()
        {
            Stops = plan.Stops,
            StartLocation = plan.StartLocation,
            StartTime = plan.StartTime,
            NgoArrival = plan.NgoArrival,
            NgoLegDistanceKm = plan.NgoLegDistanceKm,
            NgoLegDurationMinutes = plan.NgoLegDurationMinutes,
            TotalDistanceKm = plan.TotalDistanceKm,
            TotalDurationMinutes = plan.TotalDurationMinutes,
            TotalWeightKg = plan.TotalWeightKg,
            KnapsackValue = plan.KnapsackValue,
            ProvenOptimal = true
        };

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
