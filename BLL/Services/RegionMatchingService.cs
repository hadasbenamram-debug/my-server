using BLL.Algorithms;
using BLL.Configuration;
using DAL;
using DAL.Classes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>מתנדב + חלון זמינות אחד שלו שמתאים לתרומה (אזור המוצא ואזור העמותה).</summary>
    public sealed record VolunteerAvailability(Volunteer Volunteer, AvailabilityWindow Window);

    public class RegionAvailabilityMatchingService
    {
        private readonly IMatchingPolicyProvider _policyProvider;
        private readonly ILogger<RegionAvailabilityMatchingService> _logger;

        public RegionAvailabilityMatchingService(
            ILogger<RegionAvailabilityMatchingService>? logger = null,
            IMatchingPolicyProvider? policy = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RegionAvailabilityMatchingService>.Instance;
            _policyProvider = policy ?? new StaticMatchingPolicyProvider();
        }

        /// <summary>
        /// תאימות: מתנדבים שזמינים <b>עכשיו</b> (asOf) לנסיעה מאזור התרומה לעמותה הנתונה.
        /// </summary>
        public List<Volunteer> FindCandidates(
            Donation donation,
            Ngo targetNgo,
            IEnumerable<Volunteer> allVolunteers,
            DateTime asOf)
        {
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));

            return FindAvailability(donation, new[] { targetNgo }, allVolunteers, asOf, asOf, asOf)
                .Select(a => a.Volunteer)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// תיקון 24: כל חלונות הזמינות שמתחילים עד windowToUtc ומסתיימים אחרי windowFromUtc - לא רק "זמין עכשיו".
        /// כך משמרת שמתחילה בעוד חצי שעה נכנסת לתכנון (לפי מערכת השעות של המתנדב), במקום להחמיץ אותה בין שתי ריצות.
        ///
        /// החלון מתאים אם המשבצת משרתת נסיעה מאזור התרומה לאזור של לפחות אחת מהעמותות האפשריות.
        ///
        /// תיקון 29: מתנדב עם GPS עדכני נפסל כאן רק אם גם בחסם התחתון (קו אווירי במהירות המקסימלית) הוא לא מגיע
        /// עד מועד האיסוף. בעבר נעשה שימוש בהערכה "ממוצעת" (40 קמ"ש x 1.3) שפסלה מתנדבים שבפועל, בכביש מהיר,
        /// מגיעים בזמן - עוד לפני ש-Google נשאל. ההחלטה המדויקת מתקבלת בבניית המסלול.
        /// </summary>
        public List<VolunteerAvailability> FindAvailability(
            Donation donation,
            IReadOnlyCollection<Ngo> candidateNgos,
            IEnumerable<Volunteer> allVolunteers,
            DateTime asOfUtc,
            DateTime windowFromUtc,
            DateTime windowToUtc)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));
            if (candidateNgos == null) throw new ArgumentNullException(nameof(candidateNgos));
            if (allVolunteers == null) throw new ArgumentNullException(nameof(allVolunteers));

            var policy = _policyProvider.Current;
            var donationRegions = new[] { donation.RegionId };
            var ngoRegions = candidateNgos.Select(n => n.RegionId).Distinct().ToList();
            var pickupDeadline = NgoAvailabilityHelper.GetPickupDateTime(donation);
            var result = new List<VolunteerAvailability>();

            foreach (var v in allVolunteers)
            {
                if (!v.IsAvailable || v.AvailabilitySlots == null || v.AvailabilitySlots.Count == 0)
                    continue;

                bool hasFreshGps = (v.CurrentLatitude != 0 || v.CurrentLongitude != 0) &&
                                   v.LocationUpdatedAt.HasValue &&
                                   (asOfUtc - v.LocationUpdatedAt.Value).TotalMinutes <= policy.MaxLocationAgeMinutes;

                double lowerBoundMinutes = hasFreshGps
                    ? GeoUtils.LowerBoundDriveMinutes(
                        GeoUtils.HaversineKm(v.CurrentLatitude, v.CurrentLongitude, donation.Latitude, donation.Longitude),
                        policy.MaxRoadSpeedKmH)
                    : 0;

                foreach (var window in AvailabilityHelper.GetWindows(v, windowFromUtc, windowToUtc))
                {
                    if (!ngoRegions.Any(r => AvailabilityHelper.SlotServesRoute(window.Slot, donationRegions, r)))
                        continue;

                    var departure = window.StartUtc > windowFromUtc ? window.StartUtc : windowFromUtc;
                    if (departure > window.EndUtc)
                        continue;

                    if (departure.AddMinutes(lowerBoundMinutes) > pickupDeadline)
                    {
                        _logger.LogDebug("Volunteer {VolunteerId} cannot reach donation {DonationId} before its pickup deadline even on the fastest road.", v.Id, donation.Id);
                        continue;
                    }

                    result.Add(new VolunteerAvailability(v, window));
                }
            }

            _logger.LogDebug("FindAvailability: donation {DonationId} - {Count} volunteer availability windows.", donation.Id, result.Count);
            return result;
        }
    }
}
