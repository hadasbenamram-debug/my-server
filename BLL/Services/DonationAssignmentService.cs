using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Integrations.GoogleMaps;
using DAL;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// שירות ההקצאה הראשי: מקבל תרומה, מסנן מתנדבים זמינים בלבד,
    /// מחשב מרחקים (עם מטמון וגיבוי Haversine), מדרג ומבצע הקצאה אטומה.
    ///
    /// תיקון עקביות: הגרסה המקורית קבעה volunteer.IsAvailable = false כאן, מיד עם
    /// יצירת ה-Match, בעוד ש-MatchCreationService.CreateMatchAsync (בכוונה, לפי הערת
    /// הקוד שם) *לא* מעדכן את IsAvailable באותו רגע - רק כשהמתנדב בפועל מאשר את
    /// הבקשה (Accept). שני מקומות שסותרים זה את זה לגבי "מתי מתנדב נחשב תפוס"
    /// עלולים לגרום למתנדב להיחסם משיבוצים נוספים לפני שאישר בכלל, או להישאר
    /// "פנוי" בטבלה בעוד שכבר שובץ - תלוי איזה משני המסלולים רץ. MatchCreationService
    /// הוא המקור הסמכותי (יש שם הערה מפורשת בקוד), ולכן העדכון המוקדם הוסר מכאן.
    /// </summary>
    public class DonationAssignmentService : BLL.Interfaces.IDonationAssignmentService
    {
        private readonly RegionAvailabilityMatchingService _candidateFinder;
        private readonly VolunteerRankingService _rankingService;
        private readonly BacktrackingAssignmentService _backtrackingService;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly DistanceMemoizationService _distanceCache;
        private readonly IGoogleMapsDistanceService _googleMapsDistanceService;
        private readonly double _maxSearchRadiusKm;

        public DonationAssignmentService(
            RegionAvailabilityMatchingService candidateFinder,
            VolunteerRankingService rankingService,
            BacktrackingAssignmentService backtrackingService,
            IVolunteerRepository volunteerRepository,
            DistanceMemoizationService distanceCache,
            IGoogleMapsDistanceService googleMapsDistanceService = null,
            double maxSearchRadiusKm = 15.0)
        {
            _candidateFinder = candidateFinder ?? throw new ArgumentNullException(nameof(candidateFinder));
            _rankingService = rankingService ?? throw new ArgumentNullException(nameof(rankingService));
            _backtrackingService = backtrackingService ?? throw new ArgumentNullException(nameof(backtrackingService));
            _volunteerRepository = volunteerRepository ?? throw new ArgumentNullException(nameof(volunteerRepository));
            _distanceCache = distanceCache ?? throw new ArgumentNullException(nameof(distanceCache));
            _googleMapsDistanceService = googleMapsDistanceService;
            _maxSearchRadiusKm = maxSearchRadiusKm;
        }

        public async Task<AssignmentResult> AssignVolunteerAsync(
            Donation donation,
            Ngo targetNgo,
            IEnumerable<Volunteer> allVolunteers,
            DateTime asOf)
        {
            var activeAvailableVolunteers = allVolunteers.Where(v => v.IsAvailable);
            var candidates = _candidateFinder.FindCandidates(donation, targetNgo, activeAvailableVolunteers, asOf).ToList();

            var withDistance = new List<(Volunteer Volunteer, double DistanceKm)>();
            foreach (var v in candidates)
            {
                double distanceKm = await GetDistanceKmAsync(donation, v);
                if (distanceKm <= _maxSearchRadiusKm)
                {
                    withDistance.Add((v, distanceKm));
                }
            }

            var distanceByVolunteerId = withDistance.ToDictionary(x => x.Volunteer.Id, x => x.DistanceKm);
            var ranked = _rankingService.RankVolunteers(
                withDistance.Select(x => x.Volunteer).ToList(),
                donation,
                distanceByVolunteerId,
                _maxSearchRadiusKm);

            var sortedCandidates = ranked
                .Select(v => new VolunteerCandidate(v, distanceByVolunteerId[v.Id]))
                .ToList();

            // הערה: אין כאן עוד עדכון volunteer.IsAvailable=false - ר' הערה במסמכת המחלקה.
            // ה-Match עצמו (שנוצר בהמשך ע"י MatchCreationService דרך ה-delegate שהוזרק
            // ל-BacktrackingAssignmentService) הוא זה שקובע שהמתנדב "מבוקש", וה-IsAvailable
            // יעודכן ל-false רק כשהמתנדב בפועל יאשר (Accept) את הבקשה.
            return await _backtrackingService.TryAssignDonation(donation, targetNgo, sortedCandidates, asOf);
        }

        private Task<double> GetDistanceKmAsync(Donation donation, Volunteer volunteer)
        {
            string cacheKey = DistanceMemoizationService.BuildKey(volunteer.Id, donation.Id);

            return _distanceCache.GetOrCalculateDistanceAsync(cacheKey, async () =>
            {
                if (_googleMapsDistanceService != null)
                {
                    try
                    {
                        var (distanceKm, _) = await _googleMapsDistanceService.GetDrivingDistanceAsync(
                            donation.Latitude, donation.Longitude,
                            volunteer.CurrentLatitude, volunteer.CurrentLongitude);
                        return distanceKm;
                    }
                    catch
                    {
                        // נפילה חזרה ל-Haversine במקרה של תקלה בשירות המפות
                    }
                }

                return GeoUtils.HaversineKm(donation.Latitude, donation.Longitude, volunteer.CurrentLatitude, volunteer.CurrentLongitude);
            });
        }
    }
}
