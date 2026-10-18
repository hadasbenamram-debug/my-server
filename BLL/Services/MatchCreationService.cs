using BLL.Algorithms;
using BLL.Configuration;
using BLL.Exceptions;
using BLL.Integrations.GoogleMaps;
using BLL.Notifications;
using BLL.Security;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    // יוצר מסלול + שידוך לכל תחנה (בטרנזקציה אחת) ושולח למתנדב בקשת התנדבות אחת למסלול כולו.
    public class MatchCreationService
    {
        private readonly IMatchRepository _matchRepository;
        private readonly IDonationRepository _donationRepository;
        private readonly IVolunteerRouteRepository _routeRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IActionTokenService _actionTokens;
        private readonly INotificationService _notifications;
        private readonly IGoogleMapsDistanceService? _google;
        private readonly IBackgroundJobClient _jobs;
        private readonly IMatchingPolicyProvider _policy;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MatchCreationService> _logger;

        public MatchCreationService(
            IMatchRepository matchRepository,
            IDonationRepository donationRepository,
            IVolunteerRouteRepository routeRepository,
            IUserRepository userRepository,
            IUnitOfWork unitOfWork,
            IActionTokenService actionTokens,
            INotificationService notifications,
            IBackgroundJobClient jobs,
            IMatchingPolicyProvider policy,
            IConfiguration configuration,
            ILogger<MatchCreationService> logger,
            IGoogleMapsDistanceService? google = null)
        {
            _matchRepository = matchRepository;
            _donationRepository = donationRepository;
            _routeRepository = routeRepository;
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _actionTokens = actionTokens;
            _notifications = notifications;
            _jobs = jobs;
            _policy = policy;
            _configuration = configuration;
            _logger = logger;
            _google = google;
        }

        /// <summary>
        /// שומר מסלול ושידוכים לכל התחנות שלו. מחזיר null אם אחת התרומות כבר שובצה במקביל
        /// (ואז שום דבר לא נשמר).
        /// </summary>
        public async Task<VolunteerRoute?> CreateRouteAsync(Volunteer volunteer, Ngo ngo, RoutePlan plan, double matchScore)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (ngo == null) throw new ArgumentNullException(nameof(ngo));
            if (plan == null || plan.Stops.Count == 0) throw new ArgumentException("המסלול ריק.", nameof(plan));

            var policy = _policy.Current;
            var now = DateTime.UtcNow;

            // תוקן: ResponseDeadline לא מולא אף פעם, ולכן ה-Timeout לא הופעל. עכשיו הוא נקבע לכל שידוך.
            var deadline = now.AddMinutes(Math.Max(1, policy.ResponseTimeoutMinutes));

            // Polyline של המסלול המלא (אופציונלי - רק אם Google זמין). מחוץ לטרנזקציה.
            EncodedRoute? encoded = null;
            if (_google != null)
            {
                try
                {
                    encoded = await _google.GetRouteAsync(
                        new GeoPoint(plan.StartLocation.Latitude, plan.StartLocation.Longitude),
                        plan.Stops.Select(s => new GeoPoint(s.Location.Latitude, s.Location.Longitude)).ToList(),
                        new GeoPoint(ngo.Latitude, ngo.Longitude));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get route polyline from Google.");
                }
            }

            VolunteerRoute? route = null;
            var createdMatches = new List<Match>();
            string? linkToken = null;

            try
            {
                bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    route = new VolunteerRoute
                    {
                        VolunteerId = volunteer.Id,
                        ScheduledAt = now,
                        TotalWeightKg = plan.TotalWeightKg,
                        Status = RouteStatus.Planned,
                        GoogleDirectionsPolyline = encoded?.Polyline,
                        EstimatedTotalDurationMinutes = encoded?.DurationMinutes > 0 ? encoded.DurationMinutes : plan.TotalDurationMinutes,
                        EstimatedTotalDistanceKm = encoded?.DistanceKm > 0 ? encoded.DistanceKm : plan.TotalDistanceKm,
                        MatchScore = Math.Round(matchScore, 2)
                    };
                    await _routeRepository.AddAsync(route);

                    foreach (var stop in plan.Stops)
                    {
                        // ערכים עדכניים מהמסד (כולל RowVersion) - ואם בינתיים מישהו אחר שיבץ את התרומה, מבטלים הכל
                        var donation = await _donationRepository.GetFreshAsync(stop.DonationId);
                        if (donation == null || donation.Status != DonationStatus.Pending)
                            throw new ConflictException($"התרומה {stop.DonationId} כבר אינה ממתינה לשיבוץ.");

                        var match = new Match
                        {
                            DonationId = donation.Id,
                            NgoId = ngo.Id,
                            VolunteerId = volunteer.Id,
                            VolunteerRouteId = route.Id,
                            Status = MatchStatus.AwaitingVolunteerConfirmation,
                            IsActive = true,
                            AssignedAt = now,
                            EstimatedArrival = stop.EstimatedArrival,
                            ResponseDeadline = deadline,
                            MatchScore = Math.Round(matchScore, 2),
                            GoogleMapsDistanceKm = stop.LegDistanceKm,
                            GoogleMapsDurationMinutes = stop.LegDurationMinutes
                        };
                        await _matchRepository.AddAsync(match);
                        createdMatches.Add(match);

                        donation.Status = DonationStatus.Matched;
                        await _donationRepository.UpdateAsync(donation);
                    }

                    // קישור במייל: טוקן חד-פעמי שמוגבל למסלול הזה ולפעולת אישור/דחייה בלבד (לא טוקן התחברות)
                    linkToken = await _actionTokens.CreateLinkTokenAsync(
                        volunteer.UserId, ActionTokenPurpose.MatchResponse, deadline - now + TimeSpan.FromMinutes(1), route.Id);

                    await QueueVolunteerRequestAsync(volunteer, ngo, route, plan, linkToken, deadline);
                });

                if (!saved)
                    return null;
            }
            catch (ConflictException ex)
            {
                _logger.LogInformation("Route for volunteer {VolunteerId} was not saved: {Reason}", volunteer.Id, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                // למשל האינדקס הייחודי (שידוך פעיל אחד לתרומה) - שידוך מקביל ניצח
                _logger.LogWarning(ex, "Route for volunteer {VolunteerId} could not be saved.", volunteer.Id);
                return null;
            }

            route!.Matches = createdMatches;

            await _notifications.NotifyUserAsync(
                volunteer.UserId,
                "בקשת התנדבות חדשה",
                $"מסלול עם {createdMatches.Count} תחנות איסוף. יש לאשר עד {LocalClock.ToLocal(deadline):HH:mm}.",
                link: $"/app/volunteer/incoming-request/{createdMatches[0].Id}",
                type: "request");

            // בדיקת Timeout בדיוק כשהזמן נגמר (בנוסף לג'וב התקופתי). אין פרמטר זמן -
            // הזמן הנוכחי נקבע בתוך הג'וב כשהוא רץ, לא ברגע התזמון.
            try
            {
                _jobs.Schedule<MatchTimeoutService>(s => s.ProcessTimeoutsAsync(), deadline - now + TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not schedule timeout check; the recurring job will handle it.");
            }

            return route;
        }

        // בקשת ההתנדבות כוללת את כל פרטי המסלול (מסמך התיאור, שלב 5)
        private async Task QueueVolunteerRequestAsync(Volunteer volunteer, Ngo ngo, VolunteerRoute route, RoutePlan plan, string linkToken, DateTime deadline)
        {
            var user = volunteer.User ?? await _userRepository.GetByIdAsync(volunteer.UserId);
            if (user == null)
                return;

            var link = FrontendLinks.TryBuild(_configuration,
                $"app/respond?token={WebUtility.UrlEncode(linkToken)}");

            var stopsHtml = string.Join("", plan.Stops.Select((s, i) =>
                $"<li>תחנה {i + 1}: תרומה #{s.DonationId}, הגעה משוערת {LocalClock.ToLocal(s.EstimatedArrival ?? plan.StartTime):HH:mm}</li>"));

            var body =
                $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>נמצא עבורך מסלול איסוף חדש:<br/>" +
                $"<ul>{stopsHtml}</ul>" +
                $"יעד: {WebUtility.HtmlEncode(ngo.NgoName)} - {WebUtility.HtmlEncode(ngo.Address)}<br/>" +
                $"משקל כולל: {plan.TotalWeightKg:0.#} ק\"ג, הגעה משוערת לעמותה: {LocalClock.ToLocal(plan.NgoArrival):HH:mm}<br/><br/>" +
                $"יש לאשר או לדחות עד <b>{LocalClock.ToLocal(deadline):HH:mm}</b>." +
                (link == null ? "" : $"<br/><a href='{link}'>לאישור או דחייה של המסלול</a>");

            await _notifications.QueueEmailAsync(user.Id, user.Email, "בקשת התנדבות חדשה - לתת באהבה", body,
                summary: $"בקשת התנדבות למסלול #{route.Id}");

            // SMS רק למספר שאומת
            if (user.IsPhoneVerified && !string.IsNullOrWhiteSpace(user.Phone))
            {
                await _notifications.QueueSmsAsync(user.Id, user.Phone,
                    $"לתת באהבה: בקשת איסוף חדשה ({plan.Stops.Count} תחנות). יש לאשר עד {LocalClock.ToLocal(deadline):HH:mm}" +
                    (link == null ? "." : $": {link}"),
                    summary: $"SMS: בקשת התנדבות למסלול #{route.Id}");
            }
        }
    }
}
