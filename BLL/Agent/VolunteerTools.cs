using BLL.Algorithms;
using BLL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Agent
{
    public sealed record RouteStop(int Order, int DonationId, string Supplier, string? Address, string Status,
        string? EstimatedArrival, string LatestPickupTime, string Items, double WeightKg);

    public sealed record ActiveRoute(
        int RouteId,
        string Status,
        bool AwaitingMyResponse,
        string? RespondUntil,
        string Destination,
        string? DestinationAddress,
        string? EstimatedArrivalAtNgo,
        double TotalWeightKg,
        double? DistanceKm,
        double? DurationMinutes,
        List<RouteStop> Stops);

    public sealed record WeeklySlot(string Day, string From, string To, string Region, string? DeliversToRegion);

    public sealed record DeliveryHistoryItem(int DonationId, string Date, string Supplier, string Ngo, string Status);

    public sealed record DeliveryHistory(int CompletedTotal, int CompletedThisMonth, List<DeliveryHistoryItem> Recent);

    // כלים של מתנדב: המסלול הפעיל, הזמינות השבועית וההיסטוריה
    public sealed class VolunteerTools
    {
        private readonly IMatchService _matches;
        private readonly IVolunteerService _volunteers;
        private readonly AgentCaller _caller;
        private readonly TimeProvider _clock;

        public VolunteerTools(IMatchService matches, IVolunteerService volunteers, AgentCaller caller, TimeProvider clock)
        {
            _matches = matches;
            _volunteers = volunteers;
            _caller = caller;
            _clock = clock;
        }

        [Description("מחזיר את המסלול הפעיל של המתנדב המחובר: האם הוא ממתין לאישור שלו ועד מתי, תחנות האיסוף לפי הסדר " +
                     "(ספק, כתובת, מצב, זמן הגעה משוער, שעת איסוף אחרונה, מה אוספים), העמותה שאליה מביאים וזמן ההגעה אליה. " +
                     "מחזיר null אם אין כרגע מסלול פעיל.")]
        public async Task<ActiveRoute?> GetMyActiveRouteAsync()
        {
            var route = await _matches.GetMyActiveRouteAsync(_caller.User);
            if (route == null)
                return null;

            return new ActiveRoute(
                route.Id,
                AgentFormat.Route(route.Status),
                route.AwaitingResponse,
                route.AwaitingResponse ? AgentFormat.DateAndTime(route.ResponseDeadline) : null,
                route.NgoName ?? "",
                route.NgoAddress,
                AgentFormat.DateAndTime(route.EstimatedNgoArrival),
                Math.Round(route.TotalWeightKg, 1),
                route.EstimatedTotalDistanceKm.HasValue ? Math.Round(route.EstimatedTotalDistanceKm.Value, 1) : null,
                route.EstimatedTotalDurationMinutes.HasValue ? Math.Round(route.EstimatedTotalDurationMinutes.Value) : null,
                route.Stops
                    .OrderBy(s => s.Order)
                    .Select(s => new RouteStop(
                        s.Order,
                        s.DonationId,
                        s.SupplierName ?? "",
                        s.Address,
                        AgentFormat.Match(s.Status),
                        AgentFormat.DateAndTime(s.EstimatedArrival),
                        AgentFormat.Time(s.LatestPickupTime),
                        DonationTools.DescribeItems(s.Items),
                        Math.Round(s.WeightKg, 1)))
                    .ToList());
        }

        [Description("מחזיר את הזמינות השבועית שהמתנדב המחובר הגדיר: יום, משעה, עד שעה, אזור, ואזור יעד אם הוא מוכן להוביל בין אזורים. " +
                     "לפי הזמינות הזו המערכת שולחת לו הצעות למסלולים.")]
        public async Task<List<WeeklySlot>> GetMyAvailabilityAsync()
        {
            var slots = await _volunteers.GetMyAvailabilityAsync(_caller.UserId);

            return slots
                .OrderBy(s => s.Day)
                .ThenBy(s => s.StartTime, StringComparer.Ordinal)
                .Select(s => new WeeklySlot(AgentFormat.AvailabilityDay(s.Day), s.StartTime, s.EndTime, s.RegionName, s.ToRegionName))
                .ToList();
        }

        [Description("מחזיר סיכום של המשלוחים של המתנדב המחובר: כמה השלים בסך הכול וכמה החודש, והמשלוחים האחרונים.")]
        public async Task<DeliveryHistory> GetMyDeliveryHistoryAsync(
            [Description("כמה משלוחים אחרונים להחזיר, בין 1 ל-20. אם המשתמש לא ביקש מספר - 5")] int recentCount)
        {
            var history = await _matches.GetMyVolunteerHistoryAsync(_caller.User);
            var completed = history.Where(m => m.Status == MatchStatus.Completed).ToList();

            var nowLocal = LocalClock.ToLocal(_clock.GetUtcNow().UtcDateTime);
            int thisMonth = completed.Count(m =>
            {
                var local = LocalClock.ToLocal(m.DeliveredAt ?? m.CreatedAt);
                return local.Year == nowLocal.Year && local.Month == nowLocal.Month;
            });

            var recent = history
                .OrderByDescending(m => m.DeliveredAt ?? m.CreatedAt)
                .Take(Math.Clamp(recentCount, 1, 20))
                .Select(m => new DeliveryHistoryItem(
                    m.DonationId,
                    AgentFormat.DateAndTime(m.DeliveredAt ?? m.CreatedAt),
                    m.SupplierName ?? "",
                    m.NgoName ?? "",
                    AgentFormat.Match(m.Status.ToString())))
                .ToList();

            return new DeliveryHistory(completed.Count, thisMonth, recent);
        }
    }
}
