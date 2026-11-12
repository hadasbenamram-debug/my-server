using BLL.Algorithms;
using BLL.Configuration;
using BLL.Services;
using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// בחירת העמותה.
    ///
    /// תיקון 20: בעבר נבחרה עמותה אחת לפני שנבחר מתנדב, לפי ציון קבוע (+50 לאותו אזור, +30 לפי עומס, פחות ק"מ),
    /// וזמן ההגעה הוערך כנסיעה ישירה מהתרומה - בלי הדרך של המתנדב אל התרומה. כשהמסלול בפועל הגיע אליה אחרי
    /// הסגירה, לא נוסתה עמותה אחרת והתרומה הוסלמה למנהל. עכשיו השירות מחזיר את כל העמותות האפשריות
    /// (RankNgos), והמנוע בונה מסלול לכמה מהן לכל מתנדב ובוחר לפי המסלול האמיתי.
    ///
    /// תיקון 21: DailyCapacity הוא בק"ג (כך בטופס ההרשמה של העמותה - "קיבולת יומית (ק"ג)"), אבל הושווה למספר
    /// השידוכים של היום - כולל הצעות שנדחו / לא נענו. עכשיו: ק"ג, רק שידוכים חיים / שהושלמו, וכמגבלה קשה.
    /// DailyCapacity של 0 ומטה = ללא הגבלה (נתון חסר לא חוסם עמותה).
    /// </summary>
    public class NgoSelectionService : INgoSelectionService
    {
        private readonly IMatchRepository _matchRepository;
        private readonly ILogger<NgoSelectionService> _logger;
        private readonly IMatchingPolicyProvider _policyProvider;

        public NgoSelectionService(IMatchRepository matchRepository, IMatchingPolicyProvider policy, ILogger<NgoSelectionService> logger)
        {
            _matchRepository = matchRepository;
            _policyProvider = policy;
            _logger = logger;
        }

        public static bool HasCapacityLimit(Ngo ngo) => ngo.DailyCapacity > 0;

        public async Task<Dictionary<int, double>> GetRemainingCapacityKgAsync(IEnumerable<Ngo> ngos, DateTime asOfUtc)
        {
            var list = ngos.ToList();
            var localDay = LocalClock.ToLocal(asOfUtc).Date;
            var usedKg = await _matchRepository.SumWeightKgByNgoOnDateAsync(
                list.Select(n => n.Id), LocalClock.ToUtc(localDay), LocalClock.ToUtc(localDay.AddDays(1)));

            var remaining = new Dictionary<int, double>();
            foreach (var ngo in list.Where(HasCapacityLimit))
                remaining[ngo.Id] = Math.Max(0, ngo.DailyCapacity - (usedKg.TryGetValue(ngo.Id, out var kg) ? kg : 0));

            // עמותה בלי מגבלה לא מופיעה במילון = ללא הגבלה
            return remaining;
        }

        public List<NgoOption> RankNgos(Donation donation, IEnumerable<Ngo> candidateNgos,
            IReadOnlyDictionary<int, double> remainingKg, DateTime earliestPickupUtc)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));

            var policy = _policyProvider.Current;
            double weightKg = donation.TotalWeightKg();
            var options = new List<NgoOption>();

            // תיקון 43: האיסוף לא לפני "זמינה לאיסוף החל מ-"
            var readyAt = NgoAvailabilityHelper.GetEarliestPickupDateTime(donation);
            var pickupFrom = readyAt > earliestPickupUtc ? readyAt : earliestPickupUtc;

            foreach (var ngo in candidateNgos ?? Enumerable.Empty<Ngo>())
            {
                double remaining = remainingKg != null && remainingKg.TryGetValue(ngo.Id, out var left) ? left : double.PositiveInfinity;
                if (remaining + 1e-9 < weightKg)
                {
                    _logger.LogDebug("NGO {NgoId} has {Remaining:0.#} kg left today - not enough for donation {DonationId} ({Weight:0.#} kg).", ngo.Id, remaining, donation.Id, weightKg);
                    continue;
                }

                // תנאי הכרחי בלבד (חסם תחתון לנסיעה): העמותה פתוחה מתישהו בין ההגעה המוקדמת האפשרית לבין התפוגה.
                // הבדיקה המדויקת - במסלול עצמו (RoutePlanner).
                double distanceKm = GeoUtils.HaversineKm(donation.Latitude, donation.Longitude, ngo.Latitude, ngo.Longitude);
                var earliestArrival = pickupFrom.AddMinutes(GeoUtils.LowerBoundDriveMinutes(distanceKm, policy.MaxRoadSpeedKmH));
                var openArrival = NgoAvailabilityHelper.EarliestOpenAtOrAfter(ngo, earliestArrival, donation.ExpirationTime);
                if (!openArrival.HasValue)
                {
                    _logger.LogDebug("NGO {NgoId} is closed from the earliest possible arrival until donation {DonationId} expires.", ngo.Id, donation.Id);
                    continue;
                }

                options.Add(new NgoOption(ngo, distanceKm, openArrival.Value, remaining));
            }

            // הכי מוקדם בשעות הקבלה, ואז הכי קרוב, ואז מי שנשאר לה יותר מקום (פיזור העומס בין העמותות)
            return options
                .OrderBy(o => o.EarliestArrivalUtc)
                .ThenBy(o => o.DistanceKm)
                .ThenByDescending(o => o.RemainingKg)
                .ThenBy(o => o.Ngo.Id)
                .ToList();
        }

        // תאימות: העמותה הראשונה בדירוג (המנוע עצמו בונה מסלולים לכמה עמותות ובוחר לפי המסלול)
        public async Task<(Ngo Ngo, double DistanceKm, DateTime EstimatedArrival)?> SelectBestNgoAsync(
            Donation donation, IEnumerable<Ngo> candidateNgos, DateTime pickupDateTime)
        {
            var ngos = candidateNgos as IList<Ngo> ?? candidateNgos.ToList();
            if (ngos.Count == 0)
            {
                _logger.LogInformation("NgoSelectionService: No candidate NGOs provided for donation {DonationId}.", donation.Id);
                return null;
            }

            var earliestPickup = pickupDateTime > donation.CreatedAt ? pickupDateTime : donation.CreatedAt;
            var remaining = await GetRemainingCapacityKgAsync(ngos, pickupDateTime);
            var best = RankNgos(donation, ngos, remaining, earliestPickup).FirstOrDefault();

            if (best == null)
            {
                _logger.LogInformation("NgoSelectionService: No suitable NGO found for donation {DonationId}.", donation.Id);
                return null;
            }

            return (best.Ngo, best.DistanceKm, best.EarliestArrivalUtc);
        }
    }
}
