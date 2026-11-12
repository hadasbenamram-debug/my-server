using AutoMapper;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MatchStatus = DAL.Classes.MatchStatus;

namespace BLL.Services
{
    // המעקב החי של העמותה (וגם של הספקים במסלול ושל המנהל). הכול מיומן המשלוח (DeliveryLogs):
    //  - מסלול הנסיעה בפועל = רישומי המיקום מאז היציאה לדרך;
    //  - המיקום הנוכחי = רישום המיקום האחרון (רק בזמן ביצוע);
    //  - ציר הזמן = האירועים (אישור, יציאה, איסוף, מסירה, אישור קבלה).
    // זמן ההגעה לעמותה מחושב בשרת עם מפתח השרת (Google Routes) - מהמיקום הנוכחי דרך התחנות שנשארו.
    public class DeliveryTrackingService : IDeliveryTrackingService
    {
        // מסלול של שעה עם עדכון כל 10 שניות = כ-360 רישומים. 2,000 מספיק בשפע.
        private const int MaxLogRows = 2000;

        // במפה החיה של כל המסלולים מספיק לדעת מה קרה לאחרונה בכל מסלול
        private const int MaxEventsOnLiveMap = 3;

        // אותו מסלול לא נשלח ל-Google יותר מפעם בשתי דקות, גם כשכמה אנשים צופים בו
        private static readonly TimeSpan EtaCacheDuration = TimeSpan.FromMinutes(2);

        // מיקום ישן מזה (המתנדב סגר את האפליקציה / אין קליטה) - לא מחשבים ממנו זמן הגעה
        private static readonly TimeSpan LivePositionMaxAge = TimeSpan.FromMinutes(10);

        private static readonly string[] NotPickedUpYet = { nameof(MatchStatus.Confirmed), nameof(MatchStatus.InPickup) };
        private static readonly string[] PickedUp = { nameof(MatchStatus.InTransit), nameof(MatchStatus.Completed) };

        private readonly IMatchService _matchService;
        private readonly IDeliveryLogRepository _logs;
        private readonly IGoogleMapsDistanceService _distances;
        private readonly IMatchingPolicyProvider _policy;
        private readonly IMemoryCache _cache;
        private readonly IMapper _mapper;
        private readonly TimeProvider _clock;   // שעון אחד לכל המערכת (בבדיקות - שעון קבוע)

        public DeliveryTrackingService(
            IMatchService matchService,
            IDeliveryLogRepository logs,
            IGoogleMapsDistanceService distances,
            IMatchingPolicyProvider policy,
            IMemoryCache cache,
            IMapper mapper,
            TimeProvider? clock = null)
        {
            _matchService = matchService;
            _logs = logs;
            _distances = distances;
            _policy = policy;
            _cache = cache;
            _mapper = mapper;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<RouteTrackingDto?> GetRouteTrackingAsync(int routeId, CurrentUserDto currentUser)
        {
            // אותה בדיקת הרשאה כמו לפרטי המסלול עצמו (MatchService.GetRouteAsync)
            var route = await _matchService.GetRouteAsync(routeId, currentUser);
            if (route == null)
                return null;

            var tracking = await BuildAsync(route);
            await FillEtaAsync(tracking);
            return tracking;
        }

        // המפה החיה של המנהל: כל המסלולים שבדרך עכשיו, כל אחד עם מסלול הנסיעה בפועל והמיקום האחרון.
        // בלי זמן הגעה מ-Google לכל מסלול (זה היה פונה ל-Google על כל מסלול בכל רענון) - זמן ההגעה המדויק
        // נמצא במסך המעקב של המסלול עצמו (/app/track/{id}). מהאירועים נשלחים רק האחרונים.
        public async Task<List<RouteTrackingDto>> GetLiveRoutesAsync(CurrentUserDto currentUser)
        {
            // הבדיקה שהמשתמש מנהל נמצאת ב-MatchService.GetLiveRoutesAsync (ForbiddenException)
            var routes = await _matchService.GetLiveRoutesAsync(currentUser);

            var result = new List<RouteTrackingDto>();
            foreach (var route in routes)
            {
                var tracking = await BuildAsync(route);
                tracking.Events = tracking.Events.TakeLast(MaxEventsOnLiveMap).ToList();
                tracking.EstimatedNgoArrival = route.EstimatedNgoArrival;   // לפי התכנון של המנוע
                tracking.EtaSource = "plan";
                result.Add(tracking);
            }

            return result;
        }

        // המעקב של מסלול אחד מתוך יומן המשלוח: מסלול הנסיעה בפועל, המיקום האחרון, האירועים וההתקדמות
        private async Task<RouteTrackingDto> BuildAsync(RouteDto route)
        {
            var logs = await _logs.GetForRouteAsync(route.Id, MaxLogRows);
            bool live = route.Status == nameof(RouteStatus.InProgress);

            // המיקום נחשף רק מרגע היציאה לדרך. רישומים ישנים יותר (למשל רישום ידני) לא נכנסים למסלול הנסיעה.
            var trail = route.StartedAt is DateTime startedAt
                ? logs
                    .Where(l => l.Latitude.HasValue && l.Longitude.HasValue && l.Timestamp >= startedAt &&
                                GoogleStaticMapService.HasLocation(l.Latitude.Value, l.Longitude.Value))
                    .Select(l => new TrackingPointDto { Latitude = l.Latitude!.Value, Longitude = l.Longitude!.Value, At = l.Timestamp })
                    .ToList()
                : new List<TrackingPointDto>();

            var tracking = new RouteTrackingDto
            {
                Route = route,
                IsLive = live,
                CurrentPosition = live ? trail.LastOrDefault() : null,
                Trail = trail,
                Events = _mapper.Map<List<DeliveryLogResponseDto>>(logs.Where(l => l.Type != DeliveryLogType.Location).ToList()),
                TotalStops = route.Stops.Count,
                PickedUpStops = route.Stops.Count(s => PickedUp.Contains(s.Status)),
                DeliveredStops = route.Stops.Count(s => s.Status == nameof(MatchStatus.Completed)),
                GeneratedAt = _clock.GetUtcNow().UtcDateTime
            };

            return tracking;
        }

        // זמן ההגעה המשוער לעמותה
        private async Task FillEtaAsync(RouteTrackingDto tracking)
        {
            var route = tracking.Route;
            var now = _clock.GetUtcNow().UtcDateTime;

            if (route.Status == nameof(RouteStatus.Planned))
            {
                // עוד לא יצא לדרך - לפי התכנון של המנוע
                tracking.EstimatedNgoArrival = route.EstimatedNgoArrival;
                tracking.EtaSource = "plan";
                return;
            }

            var position = tracking.CurrentPosition;
            if (!tracking.IsLive || position == null || now - position.At > LivePositionMaxAge ||
                !GoogleStaticMapService.HasLocation(route.NgoLatitude, route.NgoLongitude))
                return;

            // מהמיקום הנוכחי, דרך התחנות שעוד לא נאספו, עד העמותה
            var remainingStops = route.Stops
                .Where(s => NotPickedUpYet.Contains(s.Status) && GoogleStaticMapService.HasLocation(s.Latitude, s.Longitude))
                .ToList();

            var cacheKey = $"tracking-eta:{route.Id}:{string.Join(",", remainingStops.Select(s => s.MatchId))}";
            if (!_cache.TryGetValue(cacheKey, out RemainingLeg? leg) || leg == null)
            {
                leg = await ComputeRemainingAsync(position, remainingStops, route, now);
                _cache.Set(cacheKey, leg, EtaCacheDuration);
            }

            var arrival = leg.ComputedAt.AddMinutes(leg.Minutes);
            tracking.EstimatedNgoArrival = arrival;
            tracking.RemainingMinutes = Math.Max(0, Math.Round((arrival - now).TotalMinutes, 1));
            tracking.RemainingDistanceKm = Math.Round(leg.DistanceKm, 1);
            tracking.RemainingPolyline = leg.Polyline;
            tracking.EtaSource = leg.Source;
        }

        private async Task<RemainingLeg> ComputeRemainingAsync(TrackingPointDto position, List<RouteStopDto> remainingStops, RouteDto route, DateTime now)
        {
            var origin = new GeoPoint(position.Latitude, position.Longitude);
            var stops = remainingStops.Select(s => new GeoPoint(s.Latitude, s.Longitude)).ToList();
            var destination = new GeoPoint(route.NgoLatitude, route.NgoLongitude);

            // Google Routes עם מפתח השרת (המפתח לא מגיע לדפדפן)
            var fromGoogle = await _distances.GetRouteAsync(origin, stops, destination);
            if (fromGoogle != null && fromGoogle.DurationMinutes > 0)
                return new RemainingLeg(now, fromGoogle.DurationMinutes, fromGoogle.DistanceKm, fromGoogle.Polyline, "live");

            // בלי Google: קו אווירי × מקדם עיקוף, במהירות הממוצעת שבמדיניות
            double km = 0, minutes = 0;
            var from = origin;
            foreach (var to in stops.Append(destination))
            {
                var estimate = GoogleMapsDistanceService.Estimate(from.Latitude, from.Longitude, to.Latitude, to.Longitude, _policy.Current);
                km += estimate.DistanceKm;
                minutes += estimate.DurationMinutes;
                from = to;
            }

            return new RemainingLeg(now, minutes, km, null, "estimate");
        }

        private sealed record RemainingLeg(DateTime ComputedAt, double Minutes, double DistanceKm, string? Polyline, string Source);
    }
}
