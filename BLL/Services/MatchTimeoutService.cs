using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// Hangfire job (כל דקה): מאתר Match-ים שחצו את ResponseDeadline מבלי שהמתנדב הגיב,
    /// משחרר אותם ומפעיל שיבוץ מחדש.
    ///
    /// תיקון קטן: CalculateApproximateDistance החזירה בעבר קו אווירי טהור, בעוד ש-
    /// _maxSearchRadiusKm (הנבדק בתוך BacktrackingAssignmentService) מייצג בפועל מרחק
    /// כביש. כעת מוכפל במקדם עקיפת-הכביש (RoadDetourFactor) כדי שהגיזום יהיה עקבי עם
    /// שאר המערכת ולא ייתן יתרון מלאכותי למתנדבים ב"שידוך מחדש" לעומת שידוך ראשוני.
    /// </summary>
    public class MatchTimeoutService
    {
        private readonly FoodRescueDbContext _context;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly BacktrackingAssignmentService _backtrackingService;
        private readonly RegionAvailabilityMatchingService _regionMatchingService;
        private readonly MatchingPolicyOptions _policy;
        private readonly ILogger<MatchTimeoutService> _logger;

        public MatchTimeoutService(
            FoodRescueDbContext context,
            IVolunteerRepository volunteerRepository,
            BacktrackingAssignmentService backtrackingService,
            RegionAvailabilityMatchingService regionMatchingService,
            ILogger<MatchTimeoutService> logger,
            IOptions<MatchingPolicyOptions> policy = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _volunteerRepository = volunteerRepository ?? throw new ArgumentNullException(nameof(volunteerRepository));
            _backtrackingService = backtrackingService ?? throw new ArgumentNullException(nameof(backtrackingService));
            _regionMatchingService = regionMatchingService ?? throw new ArgumentNullException(nameof(regionMatchingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _policy = policy?.Value ?? new MatchingPolicyOptions();
        }

        public async Task ProcessTimeoutsAndNoShowsAsync(DateTime asOf)
        {
            try
            {
                var expiredMatches = await _context.Matches
                    .Include(m => m.Donation)
                    .Include(m => m.Volunteer)
                    .Include(m => m.Ngo)
                    .Where(m => m.Status == MatchStatus.AwaitingVolunteerConfirmation && m.ResponseDeadline < asOf)
                    .ToListAsync();

                if (expiredMatches == null || !expiredMatches.Any())
                {
                    _logger.LogInformation("MatchTimeoutService: No expired matches found.");
                    return;
                }

                _logger.LogInformation("MatchTimeoutService: Processing {Count} expired matches.", expiredMatches.Count);

                foreach (var match in expiredMatches)
                {
                    try
                    {
                        match.Status = MatchStatus.TimedOut;
                        match.IsActive = false;
                        _context.Matches.Update(match);

                        if (match.Donation != null)
                        {
                            await ReleaseAndReassignInternalAsync(match, match.Donation, asOf);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "MatchTimeoutService: Failed to process timeout for match id={MatchId}", match.Id);
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("MatchTimeoutService: Successfully completed processing all timeouts.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MatchTimeoutService: Fatal error during ProcessTimeoutsAndNoShowsAsync execution.");
                throw;
            }
        }

        public async Task ReleaseAndReassignAsync(Match match, Donation donation, DateTime asOf)
        {
            await ReleaseAndReassignInternalAsync(match, donation, asOf);
            await _context.SaveChangesAsync();
        }

        private async Task ReleaseAndReassignInternalAsync(Match match, Donation donation, DateTime asOf)
        {
            if (match.Volunteer != null)
            {
                match.Volunteer.IsAvailable = true;
                _context.Volunteers.Update(match.Volunteer);
                _logger.LogInformation("MatchTimeoutService: Volunteer {VolunteerId} set to available due to match timeout.", match.Volunteer.Id);
            }

            donation.Status = DonationStatus.Pending;
            _context.Donations.Update(donation);
            _logger.LogInformation("MatchTimeoutService: Donation {DonationId} returned to Pending status.", donation.Id);

            var targetNgo = match.Ngo ?? await _context.Ngos
                .FirstOrDefaultAsync(n => n.RegionId == donation.RegionId);

            if (targetNgo == null)
            {
                _logger.LogWarning("MatchTimeoutService: Target NGO not found for donation {DonationId}, region {RegionId}.", donation.Id, donation.RegionId);
                return;
            }

            var allVolunteers = await _volunteerRepository.GetAllActiveAsync();
            var eligibleVolunteers = _regionMatchingService.FindCandidates(donation, targetNgo, allVolunteers, asOf);

            var sortedCandidates = eligibleVolunteers
                .Select(v => new VolunteerCandidate(v, CalculateApproximateDistance(v, donation)))
                .OrderBy(c => c.DistanceKm)
                .ToList();

            await _backtrackingService.TryAssignDonation(donation, targetNgo, sortedCandidates, asOf, 0);
            _logger.LogInformation("MatchTimeoutService: Reassignment attempt triggered for donation {DonationId}.", donation.Id);
        }

        private double CalculateApproximateDistance(Volunteer volunteer, Donation donation)
        {
            if (volunteer.CurrentLatitude == 0 && volunteer.CurrentLongitude == 0)
                return double.MaxValue; // התנהגות נכונה: מתנדב בלי GPS יורד לסוף התור

            var straightLineKm = GeoUtils.HaversineKm(volunteer.CurrentLatitude, volunteer.CurrentLongitude, donation.Latitude, donation.Longitude);
            return straightLineKm * Math.Max(_policy.RoadDetourFactor, 1.0);
        }
    }
}
