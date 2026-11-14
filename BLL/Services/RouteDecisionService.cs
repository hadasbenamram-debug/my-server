using BLL.Interfaces;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BLL.Services
{
    // =====================================================================
    // תיקון 75: "למה המנוע החליט כך"
    // ההסבר נבנה במנוע (DonationMatchingBatchService.DecisionJsonAsync) ברגע יצירת המסלול ונשמר כ-JSON
    // ב-VolunteerRoute.DecisionJson. כאן רק קוראים אותו, ומשלימים פרטים שלא היו בזיכרון של המנוע
    // (שמות הספקים, הסטטוס הנוכחי של המסלול).
    // =====================================================================

    /// <summary>שמירה וקריאה של הסבר ההחלטה - פורמט אחד לשני הצדדים</summary>
    public static class RouteDecisionJson
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public static string Serialize(RouteDecisionDto decision) => JsonSerializer.Serialize(decision, Options);

        /// <summary>null = אין הסבר, או JSON פגום (הסבר לא מכשיל את התצוגה)</summary>
        public static RouteDecisionDto? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<RouteDecisionDto>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public sealed class RouteDecisionService : IRouteDecisionService
    {
        private readonly IVolunteerRouteRepository _routes;

        public RouteDecisionService(IVolunteerRouteRepository routes)
        {
            _routes = routes;
        }

        public async Task<List<RouteDecisionSummaryDto>> GetRecentAsync(int take)
        {
            var routes = await _routes.GetRecentWithDecisionAsync(Math.Clamp(take, 1, 100));
            var list = new List<RouteDecisionSummaryDto>(routes.Count);

            foreach (var route in routes)
            {
                var decision = RouteDecisionJson.Deserialize(route.DecisionJson);
                if (decision == null) continue;

                var matches = route.Matches?.ToList() ?? new();
                list.Add(new RouteDecisionSummaryDto
                {
                    RouteId = route.Id,
                    DecidedAt = decision.DecidedAt,
                    Mode = decision.Mode,
                    RouteStatus = route.Status.ToString(),
                    VolunteerName = route.Volunteer?.User?.Name,
                    NgoName = matches.Select(m => m.Ngo?.NgoName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? decision.Route.NgoName,
                    DonationIds = (matches.Count > 0 ? matches.Select(m => m.DonationId) : decision.Route.Stops.Select(s => s.DonationId))
                        .Distinct().OrderBy(id => id).ToList(),
                    WeightKg = Math.Round(route.TotalWeightKg, 1),
                    MatchScore = Math.Round(route.MatchScore, 1),
                    VolunteersConsidered = decision.Funnel.Count > 0 ? decision.Funnel[0].Count : decision.Candidates.Count,
                    CandidatesShown = decision.Candidates.Count
                });
            }

            return list;
        }

        public async Task<RouteDecisionDto?> GetAsync(int routeId)
        {
            var route = await _routes.GetWithDetailsAsync(routeId);
            var decision = RouteDecisionJson.Deserialize(route?.DecisionJson);
            if (route == null || decision == null) return null;

            decision.RouteId = route.Id;
            decision.RouteStatus = route.Status.ToString();
            decision.VolunteerName = route.Volunteer?.User?.Name;

            var matches = route.Matches?.ToList() ?? new();
            foreach (var stop in decision.Route.Stops)
                stop.SupplierName = matches.FirstOrDefault(m => m.DonationId == stop.DonationId)?.Donation?.Supplier?.BusinessName;

            return decision;
        }

        public Task<int?> FindRouteForDonationAsync(int donationId) =>
            _routes.GetLatestDecisionRouteIdForDonationAsync(donationId);
    }
}
