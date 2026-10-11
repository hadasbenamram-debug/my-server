using BLL.Configuration;
using BLL.Interfaces;
using BLL.Integrations.GoogleMaps;
using DAL.Classes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BLL.Algoritems;

namespace BLL.Algorithms
{
    /// <summary>
    /// Hangfire job (כל שעה): מעבד את כל התרומות הממתינות לפי סדר עדיפות, ולכל אחת מנסה
    /// לבנות ולשבץ מסלול איסוף.
    /// </summary>
    public class DonationMatchingBatchService
    {
        private readonly IDonationRepository _donationRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IRegionRepository _regionRepository; // הוספת רפוזיטורי אזורים לשליפת קואורדינטות מרכז אזור
        private readonly INgoSelectionService _ngoSelectionService;
        private readonly PriorityHeapService<Donation, int> _priorityHeap;
        private readonly DonationMatchingOrchestrator _orchestrator;
        private readonly BLL.Services.MatchCreationService _matchCreationService;
        private readonly RegionAvailabilityMatchingService _regionMatcher;
        private readonly VolunteerRankingService _rankingService;
        private readonly DistanceMemoizationService _distanceCache;
        private readonly IGoogleMapsDistanceService _googleMapsDistanceService;
        private readonly MatchingPolicyOptions _policy;
        private readonly ILogger<DonationMatchingBatchService> _logger;

        public DonationMatchingBatchService(
            IDonationRepository donationRepository,
            INgoRepository ngoRepository,
            IVolunteerRepository volunteerRepository,
            IRegionRepository regionRepository,
            INgoSelectionService ngoSelectionService,
            PriorityHeapService<Donation, int> priorityHeap,
            DonationMatchingOrchestrator orchestrator,
            BLL.Services.MatchCreationService matchCreationService,
            RegionAvailabilityMatchingService regionMatcher,
            VolunteerRankingService rankingService,
            DistanceMemoizationService distanceCache,
            IGoogleMapsDistanceService googleMapsDistanceService,
            ILogger<DonationMatchingBatchService> logger,
            IOptions<MatchingPolicyOptions> policy = null)
        {
            _donationRepository = donationRepository;
            _ngoRepository = ngoRepository;
            _volunteerRepository = volunteerRepository;
            _regionRepository = regionRepository;
            _ngoSelectionService = ngoSelectionService;
            _priorityHeap = priorityHeap;
            _orchestrator = orchestrator;
            _matchCreationService = matchCreationService;
            _regionMatcher = regionMatcher;
            _rankingService = rankingService;
            _distanceCache = distanceCache;
            _googleMapsDistanceService = googleMapsDistanceService;
            _logger = logger;
            _policy = policy?.Value ?? new MatchingPolicyOptions();
        }

        public async Task RunBatchAsync()
        {
            try
            {
                var donations = (await _donationRepository.GetAvailableDonationsAsync()).ToList();
                if (donations == null || donations.Count == 0)
                {
                    _logger.LogInformation("DonationMatchingBatch: no available donations.");
                    return;
                }

                var now = DateTime.UtcNow;
                foreach (var d in donations)
                {
                    double priority = PriorityCalculator.Calculate(d, now, _policy.UrgencyTimeConstantMinutes);
                    _priorityHeap.Insert(d, priority);
                }
                _logger.LogDebug("DonationMatchingBatch: heap count after insert={Count}", _priorityHeap.Count);
                _logger.LogInformation("DonationMatchingBatch: donations inserted into heap={Count}", _priorityHeap.Count);

                var allNgos = (await _ngoRepository.GetAllAsync()).ToList();
                var donationsById = donations.ToDictionary(d => d.Id);
                double maxSearchRadiusKm = _policy.MaxSearchRadiusKm;

                while (_priorityHeap.Count > 0)
                {
                    var donation = _priorityHeap.ExtractMax();
                    _logger.LogInformation("DonationMatchingBatch: extracted donation id={DonationId}", donation == null ? -1 : donation.Id);
                    if (donation == null) break;

                    if (donation.Status != DonationStatus.Pending)
                    {
                        _logger.LogInformation("DonationMatchingBatch: skipping donation {DonationId} with status {Status}", donation.Id, donation.Status);
                        continue;
                    }

                    try
                    {
                        var pickupDateTime = NgoAvailabilityHelper.GetPickupDateTime(donation);
                        // במקום להשתמש בשעת האיסוף האחרונה שמגלגלת את הזמן ללילה, 
                        // נשתמש בזמן הנוכחי (now) כדי לבדוק הגעה החל מעכשיו (בשעות היום שבהן העמותות פתוחות)
                        var ngoSelection = await _ngoSelectionService.SelectBestNgoAsync(donation, allNgos, now);

                        if (ngoSelection == null)
                        {
                            _logger.LogInformation("DonationMatchingBatch: no suitable NGO for donation {DonationId}", donation.Id);
                            continue;
                        }

                        var selectedNgo = ngoSelection.Value.Ngo;
                        _logger.LogInformation("DonationMatchingBatch: selected NGO {NgoId} (distance={DistanceKm}km) for donation {DonationId}", selectedNgo.Id, ngoSelection.Value.DistanceKm, donation.Id);

                        var volunteers = (await _volunteerRepository.GetAvailableVolunteersAsync()).ToList();
                        var candidates = _regionMatcher.FindCandidates(donation, selectedNgo, volunteers, now).ToList();
                        _logger.LogInformation("DonationMatchingBatch: Found {Count} candidates after region matching for donation {DonationId}", candidates.Count, donation.Id);
                        var distanceByVolunteerId = new Dictionary<int, double>();

                        foreach (var v in candidates)
                        {
                            bool hasValidLocation = (v.CurrentLatitude != 0 || v.CurrentLongitude != 0) && v.LocationUpdatedAt.HasValue;

                            if (!hasValidLocation)
                            {
                                distanceByVolunteerId[v.Id] = 0;
                                continue;
                            }

                            double roughDistance = GeoUtils.HaversineKm(donation.Latitude, donation.Longitude, v.CurrentLatitude, v.CurrentLongitude);
                            if (roughDistance > maxSearchRadiusKm * 1.5)
                            {
                                continue;
                            }

                            string key = DistanceMemoizationService.BuildKey(v.Id, donation.Id);
                            try
                            {
                                var distanceKm = await _distanceCache.GetOrCalculateDistanceAsync(key, async () =>
                                {
                                    if (_googleMapsDistanceService != null)
                                    {
                                        try
                                        {
                                            var (dKm, _) = await _googleMapsDistanceService.GetDrivingDistanceAsync(
                                                donation.Latitude, donation.Longitude,
                                                v.CurrentLatitude, v.CurrentLongitude);
                                            return dKm;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "DonationMatchingBatch: Failed to get driving distance from Google Maps for volunteer {VolunteerId}, falling back to Haversine.", v.Id);
                                        }
                                    }

                                    return roughDistance * _policy.RoadDetourFactor;
                                });

                                if (distanceKm <= maxSearchRadiusKm)
                                {
                                    distanceByVolunteerId[v.Id] = distanceKm;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "DonationMatchingBatch: Error calculating distance for volunteer {VolunteerId} and donation {DonationId}", v.Id, donation.Id);
                            }
                        }

                        var validCandidates = candidates.Where(c => distanceByVolunteerId.ContainsKey(c.Id)).ToList();
                        var rankedVolunteers = _rankingService.RankVolunteers(validCandidates, donation, distanceByVolunteerId, maxSearchRadiusKm, asOfUtc: now);
                        bool routeCreated = false;

                        foreach (var volunteer in rankedVolunteers)
                        {
                            try
                            {
                                // --- הגדרת נקודת מוצא עם Fallback חכם למרכז האזור אם ה-GPS הוא 0,0 ---
                                GeoCoordinate startLocation;
                                if (volunteer.CurrentLatitude != 0 && volunteer.CurrentLongitude != 0)
                                {
                                    startLocation = new GeoCoordinate
                                    {
                                        Latitude = volunteer.CurrentLatitude,
                                        Longitude = volunteer.CurrentLongitude
                                    };
                                }
                                else
                                {
                                    var currentDay = now.DayOfWeek;
                                    var slot = volunteer.AvailabilitySlots?.FirstOrDefault(a => a.Day == currentDay);

                                    if (slot != null && slot.RegionId > 0 && _regionRepository != null)
                                    {
                                        var region = await _regionRepository.GetByIdAsync(slot.RegionId);
                                        if (region != null)
                                        {
                                            startLocation = new GeoCoordinate
                                            {
                                                Latitude = region.CenterLatitude,
                                                Longitude = region.CenterLongitude
                                            };
                                            _logger.LogInformation("DonationMatchingBatch: Volunteer {VolunteerId} has 0,0 location. Using Region {RegionId} center coordinates ({Lat}, {Lon}).", volunteer.Id, region.Id, startLocation.Latitude, startLocation.Longitude);
                                        }
                                        else
                                        {
                                            startLocation = new GeoCoordinate { Latitude = 0, Longitude = 0 };
                                        }
                                    }
                                    else
                                    {
                                        startLocation = new GeoCoordinate { Latitude = 0, Longitude = 0 };
                                    }
                                }
                                // --------------------------------------------------------------------------

                                var optimizedRoute = await _orchestrator.ProcessVolunteerRouteAssignmentAsync(
                                    volunteer,
                                    donations,
                                    startLocation,
                                    selectedNgo,
                                    now,
                                    anchorDonationId: donation.Id
                                );

                                if (optimizedRoute == null || optimizedRoute.Count == 0)
                                {
                                    continue;
                                }

                                if (!optimizedRoute.Any(s => s.DonationId == donation.Id))
                                {
                                    continue;
                                }

                                var (success, createdRoute) = await _matchCreationService.CreateRouteMatchesAsync(
                                    volunteer,
                                    selectedNgo,
                                    optimizedRoute,
                                    donationsById
                                );

                                if (success && createdRoute != null)
                                {
                                    foreach (var stop in optimizedRoute)
                                    {
                                        if (donationsById.TryGetValue(stop.DonationId, out var matchedDonation))
                                        {
                                            _priorityHeap.Remove(matchedDonation);
                                        }
                                    }

                                    _logger.LogInformation("DonationMatchingBatch: created route {RouteId} with {Count} stops for volunteer {VolunteerId}", createdRoute.Id, createdRoute.Matches?.Count ?? 0, volunteer.Id);
                                    routeCreated = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "DonationMatchingBatch: orchestrator attempt failed for volunteer {VolunteerId}", volunteer.Id);
                            }
                        }

                        if (!routeCreated)
                        {
                            _logger.LogInformation("DonationMatchingBatch: no route could be created for donation {DonationId} this round.", donation.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "DonationMatchingBatch: failed processing donation {DonationId}", donation.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DonationMatchingBatch: fatal error");
            }
        }
    }
}