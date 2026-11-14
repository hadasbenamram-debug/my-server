using BLL.Services;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 75: "למה המנוע החליט כך" - לכל מסלול שהמנוע יוצר נשמר הסבר: משפך המועמדים, פירוק הציון,
    // למה האחרים לא נבחרו, המסלול שנבנה והבחירה הגלובלית
    public class DecisionExplanationTests
    {
        [Fact]
        public async Task EngineRun_SavesAnExplanation_WithTheChosenVolunteerAndTheRunnerUp()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var near = await h.AddVolunteerAsync("near@test");
            var far = await h.AddVolunteerAsync("far@test");
            far.CurrentLatitude = 32.15;   // כ-8 ק"מ מהתרומה - בטווח, אבל רחוק יותר
            far.CurrentLongitude = 34.80;
            await h.Db.SaveChangesAsync();
            var donation = await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync();
            Assert.Equal(1, result.RoutesCreated);

            h.Fresh();
            var route = await h.Db.VolunteerRoutes.SingleAsync();
            Assert.False(string.IsNullOrEmpty(route.DecisionJson));

            var decision = RouteDecisionJson.Deserialize(route.DecisionJson)!;
            Assert.Equal("Auto", decision.Mode);
            Assert.Equal(donation.Id, decision.AnchorDonationId);
            Assert.NotNull(decision.Global);
            Assert.True(decision.Global!.RoutesSelected >= 1);

            // משפך: מתחיל בכל המתנדבים ונגמר במתנדב אחד שנבחר, ולא גדל בדרך
            Assert.True(decision.Funnel.Count >= 3);
            Assert.Equal(1, decision.Funnel[^1].Count);
            for (int i = 1; i < decision.Funnel.Count; i++)
                Assert.True(decision.Funnel[i].Count <= decision.Funnel[i - 1].Count, $"step {i} grew");

            // הנבחר - עם ציון מפורק לרכיבים (0-1), והשני - עם סיבה
            var chosen = Assert.Single(decision.Candidates, c => c.Outcome == "Chosen");
            Assert.Equal(route.VolunteerId, chosen.VolunteerId);
            Assert.InRange(chosen.Score.Total, 0.0001, 1.0);
            Assert.InRange(chosen.Score.Distance, 0.0, 1.0);
            Assert.InRange(chosen.Score.Reliability, 0.0, 1.0);
            Assert.NotNull(chosen.RouteValue);

            var other = Assert.Single(decision.Candidates, c => c.Outcome != "Chosen");
            Assert.False(string.IsNullOrWhiteSpace(other.Reason));
            Assert.Equal(near.Id, chosen.VolunteerId);
            Assert.Equal(far.Id, other.VolunteerId);
            Assert.True(chosen.Rank < other.Rank);

            // המסלול שנבנה
            Assert.Equal(donation.Id, Assert.Single(decision.Route.Stops).DonationId);
            Assert.True(decision.Route.Stops[0].IsAnchor);
            Assert.Equal(h.Ngo.NgoName, decision.Route.NgoName);

            // משקלות הציון מנורמלים (סכום 1)
            var w = decision.Weights;
            Assert.InRange(w.Distance + w.Urgency + w.Capacity + w.Reliability, 0.999, 1.001);
        }

        [Fact]
        public async Task DecisionService_ReturnsTheExplanation_WithSupplierNames_AndListsIt()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");
            var donation = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync();
            h.Fresh();

            var service = new RouteDecisionService(h.Routes);
            var routeId = await service.FindRouteForDonationAsync(donation.Id);
            Assert.NotNull(routeId);

            var decision = await service.GetAsync(routeId!.Value);
            Assert.NotNull(decision);
            Assert.Equal(routeId.Value, decision!.RouteId);
            Assert.Equal("מאפיית בדיקה", decision.Route.Stops[0].SupplierName);
            Assert.False(string.IsNullOrEmpty(decision.VolunteerName));

            var recent = Assert.Single(await service.GetRecentAsync(20));
            Assert.Equal(routeId.Value, recent.RouteId);
            Assert.Contains(donation.Id, recent.DonationIds);
        }

        [Fact]
        public async Task ManualAssignment_SavesAManualExplanation()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var volunteer = await h.AddVolunteerAsync("vol@test");
            var donation = await h.AddDonationAsync();

            var outcome = await h.Engine.AssignToVolunteerAsync(donation.Id, volunteer.Id, ignoreSchedule: true);
            Assert.True(outcome!.Matched, outcome.Message);

            h.Fresh();
            var route = await h.Db.VolunteerRoutes.SingleAsync();
            var decision = RouteDecisionJson.Deserialize(route.DecisionJson)!;
            Assert.Equal("Manual", decision.Mode);
            Assert.False(string.IsNullOrEmpty(decision.Note));
            Assert.Equal(volunteer.Id, Assert.Single(decision.Candidates).VolunteerId);
        }

        [Fact]
        public async Task OldRoutes_WithoutAnExplanation_ReturnNull()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();
            await h.Engine.RunBatchAsync();

            var route = await h.Db.VolunteerRoutes.SingleAsync();
            route.DecisionJson = null;   // כמו מסלול שנוצר לפני התיקון
            await h.Db.SaveChangesAsync();
            h.Fresh();

            var service = new RouteDecisionService(h.Routes);
            Assert.Null(await service.GetAsync(route.Id));
            Assert.Empty(await service.GetRecentAsync(20));
        }
    }
}
