using BLL.Algorithms;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using DonationStatus = DAL.Classes.DonationStatus;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // תיקון 38: חלון האישור של 5 דקות - בדיוק 5 דקות לכל מתנדב, הצעה אחת בכל רגע, ואין אישור מאוחר
    public sealed class ResponseWindowTests
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        private static CurrentUserDto As(int userId) => new() { UserId = userId };

        private static async Task<(Volunteer First, Volunteer Second, Donation Donation)> OfferToFirstAsync(EngineHarness h)
        {
            await h.SeedBasicsAsync();
            var first = await h.AddVolunteerAsync("first@test", reliability: 5.0);
            var second = await h.AddVolunteerAsync("second@test", reliability: 1.0);
            var donation = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            return (first, second, donation);
        }

        private static async Task<Match> ActiveOfferAsync(EngineHarness h) =>
            await h.Db.Matches.SingleAsync(m => m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation);

        [Fact]
        public async Task EachOffer_GetsExactlyFiveMinutes_FromItsOwnAssignment()
        {
            using var h = new EngineHarness();
            var (_, second, _) = await OfferToFirstAsync(h);
            var firstOffer = await ActiveOfferAsync(h);
            Assert.Equal(Window, firstOffer.ResponseDeadline!.Value - firstOffer.AssignedAt);

            // הזמן של הראשון עבר -> ההצעה עוברת לשני, עם 5 דקות חדשות ומלאות משלו
            await h.Timeouts.ProcessTimeoutsAtAsync(firstOffer.ResponseDeadline.Value);
            await h.Engine.ReassignDonationAsync(firstOffer.DonationId);
            h.Fresh();

            var secondOffer = await ActiveOfferAsync(h);
            Assert.Equal(second.Id, secondOffer.VolunteerId);
            Assert.Equal(Window, secondOffer.ResponseDeadline!.Value - secondOffer.AssignedAt);
            Assert.True(secondOffer.AssignedAt >= firstOffer.AssignedAt);
            Assert.Equal(MatchStatus.TimedOut, (await h.Db.Matches.SingleAsync(m => m.Id == firstOffer.Id)).Status);
        }

        [Fact]
        public async Task ThereIsNeverMoreThanOneOpenOffer_ForADonation()
        {
            using var h = new EngineHarness();
            await OfferToFirstAsync(h);

            // ריצות נוספות של המנוע בזמן שהראשון עוד יכול לאשר - לא שולחות את התרומה למתנדב נוסף
            await h.Engine.RunBatchAsync("Manual");
            var donationId = (await ActiveOfferAsync(h)).DonationId;
            await h.Engine.ReassignDonationAsync(donationId);
            h.Fresh();

            Assert.Single(await h.Db.Matches.ToListAsync());
            Assert.Equal(DonationStatus.Matched, (await h.Db.Donations.SingleAsync()).Status);
        }

        [Fact]
        public async Task DecliningPassesTheOffer_RightAway_AndOnlyOnce()
        {
            using var h = new EngineHarness();
            var (first, second, _) = await OfferToFirstAsync(h);
            var offer = await ActiveOfferAsync(h);

            await h.MatchService.DeclineAsync(offer.Id, As(first.UserId));
            await h.MatchService.DeclineAsync(offer.Id, As(first.UserId));   // לחיצה כפולה

            Assert.Equal(1, h.Jobs.Count<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
            await h.Engine.ReassignDonationAsync(offer.DonationId);
            h.Fresh();
            Assert.Equal(second.Id, (await ActiveOfferAsync(h)).VolunteerId);
        }

        [Fact]
        public async Task AcceptingAfterTheWindow_IsRejected_EvenIfThePageStillShowsTheButton()
        {
            using var h = new EngineHarness();
            var (first, _, _) = await OfferToFirstAsync(h);
            var offer = await ActiveOfferAsync(h);

            // 5:01 - הג'וב שמסמן "לא ענה בזמן" עוד לא רץ, והדף אצל המתנדב לא רוענן
            offer.ResponseDeadline = DateTime.UtcNow.AddSeconds(-1);
            await h.Db.SaveChangesAsync();
            h.Fresh();

            var result = await h.Workflow.AcceptAsync(await h.Db.Matches.Include(m => m.Donation).SingleAsync(m => m.Id == offer.Id), first.Id);
            h.Fresh();

            Assert.Equal(BLL.Interfaces.RouteResponseResult.Expired, result);
            var saved = await h.Db.Matches.SingleAsync(m => m.Id == offer.Id);
            Assert.Equal(MatchStatus.TimedOut, saved.Status);
            Assert.False(saved.IsActive);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync()).Status);
            Assert.Equal(1, h.Jobs.Count<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));

            // הג'וב שרץ אחר כך לא מעביר את התרומה פעם שנייה
            Assert.Equal(0, await h.Timeouts.ProcessTimeoutsAtAsync(DateTime.UtcNow));
            Assert.Equal(1, h.Jobs.Count<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
        }

        [Fact]
        public async Task AtExactlyTheDeadline_TheTimeoutJobAlreadyReleasesTheOffer()
        {
            using var h = new EngineHarness();
            await OfferToFirstAsync(h);
            var offer = await ActiveOfferAsync(h);

            Assert.Equal(0, await h.Timeouts.ProcessTimeoutsAtAsync(offer.ResponseDeadline!.Value.AddMilliseconds(-1)));
            Assert.Equal(1, await h.Timeouts.ProcessTimeoutsAtAsync(offer.ResponseDeadline.Value));
        }

        [Fact]
        public async Task AcceptedInTime_TheTimeoutJobLeavesItAlone()
        {
            using var h = new EngineHarness();
            var (first, _, _) = await OfferToFirstAsync(h);
            var offer = await ActiveOfferAsync(h);

            Assert.Equal(BLL.Interfaces.RouteResponseResult.Done, (await h.MatchService.AcceptAsync(offer.Id, As(first.UserId))).Result);
            Assert.Equal(0, await h.Timeouts.ProcessTimeoutsAtAsync(offer.ResponseDeadline!.Value.AddMinutes(1)));
            h.Fresh();

            Assert.Equal(MatchStatus.Confirmed, (await h.Db.Matches.SingleAsync()).Status);
            Assert.False(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
        }

        // שני תהליכים באותו רגע: המתנדב לוחץ "אישור" והג'וב מסמן "לא ענה בזמן". רק אחד מהם נשמר.
        [Fact]
        public async Task AcceptAndTimeoutAtTheSameMoment_OnlyOneWins()
        {
            var database = Guid.NewGuid().ToString();
            using var volunteerSide = new EngineHarness(databaseName: database);
            var (first, _, _) = await OfferToFirstAsync(volunteerSide);
            using var jobSide = new EngineHarness(databaseName: database);

            // המתנדב טען את הבקשה (עוד בזמן), ובדיוק אז הג'וב שחרר אותה
            var loadedByVolunteer = await volunteerSide.Db.Matches.Include(m => m.Donation).SingleAsync();
            var timedOut = await jobSide.Workflow.DeclineAsync(
                await jobSide.Db.Matches.Include(m => m.Donation).SingleAsync(), first.Id, MatchStatus.TimedOut);
            Assert.Equal(BLL.Interfaces.RouteResponseResult.Done, timedOut);

            var accepted = await volunteerSide.Workflow.AcceptAsync(loadedByVolunteer, first.Id);

            Assert.NotEqual(BLL.Interfaces.RouteResponseResult.Done, accepted);
            jobSide.Fresh();
            Assert.Equal(MatchStatus.TimedOut, (await jobSide.Db.Matches.SingleAsync()).Status);
            Assert.Equal(DonationStatus.Pending, (await jobSide.Db.Donations.SingleAsync()).Status);
        }

        [Fact]
        public async Task TheVolunteerSeesTheTimeLeft_ByTheServerClock()
        {
            using var h = new EngineHarness();
            var (first, _, _) = await OfferToFirstAsync(h);
            var routeId = (await ActiveOfferAsync(h)).VolunteerRouteId!.Value;

            var route = (await h.MatchService.GetRouteAsync(routeId, As(first.UserId)))!;

            Assert.True(route.AwaitingResponse);
            Assert.InRange(route.ResponseSecondsLeft!.Value, 290, 300);
        }
    }
}
