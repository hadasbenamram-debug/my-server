using BLL.Algorithms;
using BLL.Configuration;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algoritems
{
    public class NgoSelectionService : INgoSelectionService
    {
        private readonly FoodRescueDbContext _context;
        private readonly MatchingPolicyOptions _policy;
        private readonly ILogger<NgoSelectionService> _logger;

        public NgoSelectionService(FoodRescueDbContext context, IOptions<MatchingPolicyOptions> policy, ILogger<NgoSelectionService> logger)
        {
            _context = context;
            _policy = policy?.Value ?? new MatchingPolicyOptions();
            _logger = logger;
        }

        public async Task<(Ngo Ngo, double DistanceKm, DateTime EstimatedArrival)?> SelectBestNgoAsync(
            Donation donation, IEnumerable<Ngo> candidateNgos, DateTime pickupDateTime)
        {
            var ngos = candidateNgos as IList<Ngo> ?? candidateNgos.ToList();
            if (ngos.Count == 0)
            {
                _logger.LogInformation("NgoSelectionService: No candidate NGOs provided for donation {DonationId}.", donation.Id);
                return null;
            }

            var ids = ngos.Select(n => n.Id).ToList();
            var today = pickupDateTime.Date;

            var loadList = await _context.Matches
                .Where(m => ids.Contains(m.NgoId) && m.AssignedAt.Date == today)
                .GroupBy(m => m.NgoId)
                .Select(g => new { NgoId = g.Key, Count = g.Count() })
                .ToListAsync();

            var loadByNgoId = loadList.ToDictionary(x => x.NgoId, x => x.Count);

            Ngo? best = null;
            double bestScore = double.MinValue, bestDistance = double.MaxValue;
            DateTime bestArrival = pickupDateTime;

            foreach (var ngo in ngos)
            {
                double distanceKm = GeoUtils.HaversineKm(
                    donation.Latitude, donation.Longitude, ngo.Latitude, ngo.Longitude);
                var travelMinutes = GeoUtils.EstimateDriveMinutes(
                    distanceKm, _policy.AverageSpeedKmH, _policy.RoadDetourFactor);
                var travel = TimeSpan.FromMinutes(travelMinutes);

                // חישוב טווח ההגעות האפשרי בדומה ל-CanNgoReceiveDonation
                var earliestPickup = pickupDateTime > donation.CreatedAt ? pickupDateTime : donation.CreatedAt;
                var latestPickup = NgoAvailabilityHelper.GetPickupDateTime(donation);

                if (earliestPickup > latestPickup)
                {
                    continue;
                }

                var earliestArrival = earliestPickup + travel;
                var latestArrival = latestPickup + travel;

                if (latestArrival > donation.ExpirationTime)
                {
                    latestArrival = donation.ExpirationTime;
                }

                // שימוש ב-Logger כדי שהפלט יופיע בדיוק כמו שאר הלוגים שלך
                var localEarliestArrival = LocalClock.ToLocal(earliestArrival);
                var localLatestArrival = LocalClock.ToLocal(latestArrival);
                _logger.LogInformation("NgoSelectionService [DEBUG Window]: Donation {DonationId} -> NGO {NgoId} | Local Earliest: {LocalEarliest} | Local Latest: {LocalLatest}",
                    donation.Id, ngo.Id, localEarliestArrival, localLatestArrival);

                if (earliestArrival > latestArrival)
                {
                    _logger.LogInformation("NgoSelectionService: NGO {NgoId} rejected for donation {DonationId} because arrival window is invalid.", ngo.Id, donation.Id);
                    continue;
                }

                // מציאת הרגע המוקדם ביותר בטווח שבו העמותה פתוחה בפועל
                var validArrival = NgoAvailabilityHelper.EarliestOpenAtOrAfter(ngo, earliestArrival, latestArrival);
                if (!validArrival.HasValue)
                {
                    _logger.LogInformation("NgoSelectionService: NGO {NgoId} rejected for donation {DonationId} because NGO is closed during the arrival window.", ngo.Id, donation.Id);
                    continue;
                }

                DateTime arrival = validArrival.Value;

                double score = 0;
                if (ngo.RegionId == donation.RegionId) score += 50.0;

                int load = loadByNgoId.TryGetValue(ngo.Id, out var c) ? c : 0;
                double capacityRatio = ngo.DailyCapacity > 0
                    ? Math.Clamp((double)(ngo.DailyCapacity - load) / ngo.DailyCapacity, 0, 1)
                    : 0;
                score += capacityRatio * 30.0;
                score -= distanceKm;

                _logger.LogDebug("NgoSelectionService: NGO {NgoId} evaluated for donation {DonationId}: score={Score}, distance={Distance}km, load={Load}/{Capacity}", ngo.Id, donation.Id, score, distanceKm, load, ngo.DailyCapacity);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = ngo;
                    bestDistance = distanceKm;
                    bestArrival = arrival;
                }
            }

            if (best == null)
            {
                _logger.LogInformation("NgoSelectionService: No suitable NGO found after checking all candidates for donation {DonationId}.", donation.Id);
                return null;
            }

            _logger.LogInformation("NgoSelectionService: Best NGO selected for donation {DonationId} is NGO {NgoId} with score {Score}.", donation.Id, best.Id, bestScore);
            return (best, bestDistance, bestArrival);
        }
    }
}