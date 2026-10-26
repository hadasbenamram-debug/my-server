using BLL.Interfaces;
using DAL;
using DAL.Classes;
using FoodRescue.Controllers;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // חלון התגובה של המתנדב (5 דקות) - תיקונים 12-14
    public class ResponseWindowTests
    {
        private static CurrentUserDto As(User user) => new() { UserId = user.Id, IsAdmin = user.Role == UserRole.Admin };

        private static async Task<(EngineHarness H, Volunteer V, User VolunteerUser, Match Match)> OfferAsync()
        {
            var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            var user = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);
            return (h, v, user, match);
        }

        // זמן התגובה עבר - אבל הג'וב שמסמן TimedOut עוד לא רץ
        private static async Task ExpireAsync(EngineHarness h)
        {
            foreach (var m in await h.Db.Matches.ToListAsync())
                m.ResponseDeadline = DateTime.UtcNow.AddSeconds(-30);
            await h.Db.SaveChangesAsync();
            h.Fresh();
        }

        private static MatchesController Controller(EngineHarness h, User user)
        {
            var controller = new MatchesController(h.MatchService, h.CreateUserService());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())
                    }, "test"))
                }
            };
            return controller;
        }

        private static string? Code(IActionResult? result) =>
            (Assert.IsAssignableFrom<ObjectResult>(result).Value as FoodRescue.Infrastructure.ApiError)?.Code;

        // ---------------- תיקון 12: כניסה אחרי 5 הדקות - בלי "נפילה" ----------------

        [Fact]
        public async Task AcceptAfterTheDeadline_ReturnsExpired_WithoutAnException()
        {
            var (h, _, user, match) = await OfferAsync();
            using var _ = h;
            await ExpireAsync(h);

            var outcome = await h.MatchService.AcceptAsync(match.Id, As(user));

            Assert.Equal(RouteResponseResult.Expired, outcome.Result);
            Assert.Null(outcome.Route);
            h.Fresh();
            Assert.NotEqual(MatchStatus.Confirmed, (await h.Db.Matches.SingleAsync()).Status);
        }

        [Fact]
        public async Task AcceptAfterTheDeadline_Endpoint_Returns409WithAClearCode()
        {
            var (h, _, user, match) = await OfferAsync();
            using var _ = h;
            await ExpireAsync(h);

            var response = await Controller(h, user).AcceptMatch(match.Id);

            var result = Assert.IsAssignableFrom<ObjectResult>(response.Result);
            Assert.Equal(409, result.StatusCode);
            Assert.Equal("Expired", Code(response.Result));
        }

        [Fact]
        public async Task DeclineTwice_SecondTimeIsNotAwaiting_WithoutAnException()
        {
            var (h, _, user, match) = await OfferAsync();
            using var _ = h;

            Assert.Equal(RouteResponseResult.Done, await h.MatchService.DeclineAsync(match.Id, As(user)));
            h.Fresh();
            Assert.Equal(RouteResponseResult.NotAwaiting, await h.MatchService.DeclineAsync(match.Id, As(user)));
            Assert.Equal(RouteResponseResult.NotAwaiting, (await h.MatchService.AcceptAsync(match.Id, As(user))).Result);
        }

        [Fact]
        public async Task ExpiredOrUsedLink_Returns409_WithoutAnException()
        {
            var (h, _, user, _) = await OfferAsync();
            using var _ = h;
            var controller = Controller(h, user);

            var view = await controller.GetRouteByLink(new RespondByLinkDto { Token = "no-such-token" });
            Assert.Equal("LinkInvalid", Code(view.Result));

            var answer = await controller.RespondByLink(new RespondByLinkDto { Token = "no-such-token", Accept = true });
            Assert.Equal("LinkInvalid", Code(answer.Result));
        }

        // ---------------- תיקון 13: אחרי 5 דקות - באמת למתנדב הבא, בלי "שעון חדש" ובלי אישור מאוחר ----------------

        [Fact]
        public async Task AfterTheDeadline_TheRouteIsNoLongerAwaiting_EvenBeforeTheTimeoutJob()
        {
            var (h, _, user, match) = await OfferAsync();
            using var _ = h;
            Assert.True((await h.MatchService.GetRouteAsync(match.VolunteerRouteId!.Value, As(user)))!.AwaitingResponse);

            await ExpireAsync(h);

            var route = await h.MatchService.GetRouteAsync(match.VolunteerRouteId!.Value, As(user));
            Assert.False(route!.AwaitingResponse);   // אין יותר כפתור "אישור" אצל המתנדב
        }

        [Fact]
        public async Task LateAccept_TimesOutRightAway_AndTheNextVolunteerGetsAFreshWindow()
        {
            var h = new EngineHarness();
            using var _ = h;
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("a@test");
            await h.AddVolunteerAsync("b@test");
            var d = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var first = await h.Db.Matches.SingleAsync();
            var firstVolunteer = await h.Db.Volunteers.SingleAsync(v => v.Id == first.VolunteerId);
            var firstUser = await h.Db.Users.SingleAsync(u => u.Id == firstVolunteer.UserId);
            await ExpireAsync(h);

            // המתנדב הראשון מנסה לאשר אחרי הזמן: לא מאושר, והבקשה משתחררת מיד (בלי לחכות לג'וב)
            Assert.Equal(RouteResponseResult.Expired, (await h.MatchService.AcceptAsync(first.Id, As(firstUser))).Result);
            h.Fresh();
            Assert.Equal(MatchStatus.TimedOut, (await h.Db.Matches.SingleAsync(m => m.Id == first.Id)).Status);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync()).Status);
            Assert.True(h.Jobs.Has<BLL.Algorithms.DonationMatchingBatchService>(nameof(BLL.Algorithms.DonationMatchingBatchService.ReassignDonationAsync)));

            // השיבוץ מחדש: למתנדב השני, עם חלון תגובה חדש משלו. הראשון לא מקבל אותה שוב
            var again = await h.Engine.MatchDonationAsync(d.Id, "Reassign");
            Assert.True(again!.Matched);
            h.Fresh();
            var second = await h.Db.Matches.SingleAsync(m => m.IsActive);
            Assert.NotEqual(first.VolunteerId, second.VolunteerId);
            Assert.True(second.ResponseDeadline > DateTime.UtcNow.AddMinutes(4));
        }

        [Fact]
        public async Task TheTimeoutCheck_RunsOneSecondAfterTheDeadline()
        {
            var (h, _, _, match) = await OfferAsync();
            using var _ = h;

            var scheduled = h.Jobs.Jobs
                .Where(j => j.Job.Type == typeof(BLL.Algorithms.MatchTimeoutService))
                .Select(j => j.State).OfType<Hangfire.States.ScheduledState>().Single();

            var late = scheduled.EnqueueAt - match.ResponseDeadline!.Value;
            Assert.InRange(late.TotalSeconds, 0.5, 2.5);
        }

        [Fact]
        public async Task AVolunteerWhoLetADonationTimeOut_IsNotOfferedItAgain_EvenAsAnExtraStop()
        {
            var (h, v, _, first) = await OfferAsync();
            using var _ = h;
            int d1 = first.DonationId;

            // לא ענה בזמן -> שיבוץ מחדש: אין מתנדב אחר, והוא עצמו לא מקבל אותה שוב
            await h.Timeouts.ProcessTimeoutsAtAsync(first.ResponseDeadline!.Value.AddSeconds(2));
            Assert.False((await h.Engine.MatchDonationAsync(d1, "Reassign"))!.Matched);

            // תרומה חדשה ליד: הוא מקבל אותה - אבל בלי התרומה הקודמת כתחנה נוספת באותו מסלול
            var d2 = await h.AddDonationAsync(latitude: 32.0806, longitude: 34.7806);
            await h.Engine.RunBatchAsync("Manual");

            h.Fresh();
            var offered = await h.Db.Matches.Where(m => m.IsActive && m.VolunteerId == v.Id).Select(m => m.DonationId).ToListAsync();
            Assert.Contains(d2.Id, offered);
            Assert.DoesNotContain(d1, offered);
        }

        [Fact]
        public async Task LinkAnsweredAfterTheDeadline_IsExpired()
        {
            var (h, v, user, match) = await OfferAsync();
            using var _ = h;
            var token = await h.ActionTokens.CreateLinkTokenAsync(v.UserId, ActionTokenPurpose.MatchResponse, TimeSpan.FromMinutes(6),
                match.VolunteerRouteId!.Value);
            await ExpireAsync(h);

            var outcome = await h.MatchService.RespondByLinkAsync(new RespondByLinkDto { Token = token, Accept = true });

            Assert.Equal(RouteResponseResult.Expired, outcome.Result);
            var answer = await Controller(h, user).RespondByLink(new RespondByLinkDto { Token = token, Accept = true });
            Assert.IsAssignableFrom<ObjectResult>(answer.Result);
        }

        // ---------------- תיקון 14: אף פעם לא שני מתנדבים במקביל לאותה תרומה ----------------

        // בקשת המתנדב (הקשר אחד) וג'וב "לא ענה בזמן" (הקשר אחר, כמו תהליך אחר) על אותו מסד
        private static async Task<(EngineHarness Request, EngineHarness Job, Match Stale)> RaceAsync()
        {
            var name = Guid.NewGuid().ToString();
            var request = new EngineHarness(databaseName: name);
            await request.SeedBasicsAsync();
            await request.AddVolunteerAsync("a@test");
            await request.AddVolunteerAsync("b@test");
            await request.AddDonationAsync();
            await request.Engine.RunBatchAsync("Manual");
            request.Fresh();

            // הבקשה של המתנדב כבר טענה את השידוך (עדיין "ממתין") ...
            var stale = await request.Db.Matches.SingleAsync();
            return (request, new EngineHarness(databaseName: name), stale);
        }

        [Fact]
        public async Task TimeoutWinsTheRace_TheLastSecondAcceptIsNotSaved()
        {
            var (request, job, stale) = await RaceAsync();
            using var _ = request;
            using var __ = job;

            // ... ובאותו רגע הג'וב מסמן "לא ענה בזמן" ומשחרר את התרומה למתנדב הבא
            var expired = await job.Db.Matches.SingleAsync();
            Assert.Equal(RouteResponseResult.Done, await job.Workflow.DeclineAsync(expired, expired.VolunteerId, MatchStatus.TimedOut));

            // האישור (על המצב הישן) נכשל במקום לדרוס - לא "גם מאושר וגם אצל מתנדב אחר"
            Assert.Equal(RouteResponseResult.NotAwaiting, await request.Workflow.AcceptAsync(stale, stale.VolunteerId));

            job.Fresh();
            var match = await job.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.TimedOut, match.Status);
            Assert.False(match.IsActive);
            Assert.True((await job.Db.Volunteers.SingleAsync(v => v.Id == match.VolunteerId)).IsAvailable);
        }

        [Fact]
        public async Task AcceptWinsTheRace_TheTimeoutDoesNotHandTheDonationToAnotherVolunteer()
        {
            var (request, job, _) = await RaceAsync();
            using var _ = request;
            using var __ = job;

            // הג'וב טען את השידוך כשהוא עוד "ממתין" ...
            var staleInJob = await job.Db.Matches.SingleAsync();

            // ... המתנדב אישר שנייה לפני סוף הזמן ...
            var fresh = await request.Db.Matches.SingleAsync();
            Assert.Equal(RouteResponseResult.Done, await request.Workflow.AcceptAsync(fresh, fresh.VolunteerId));

            // ... והג'וב לא דורס את האישור ולא שולח את התרומה למתנדב נוסף
            Assert.Equal(RouteResponseResult.NotAwaiting, await job.Workflow.DeclineAsync(staleInJob, staleInJob.VolunteerId, MatchStatus.TimedOut));
            Assert.False(job.Jobs.Has<BLL.Algorithms.DonationMatchingBatchService>(nameof(BLL.Algorithms.DonationMatchingBatchService.ReassignDonationAsync)));

            request.Fresh();
            var match = await request.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.Confirmed, match.Status);
            Assert.True(match.IsActive);
            Assert.Equal(DonationStatus.Matched, (await request.Db.Donations.SingleAsync()).Status);
        }

        [Fact]
        public async Task Decline_HandsTheDonationOnRightAway_AndThereIsNeverMoreThanOneOpenOffer()
        {
            var h = new EngineHarness();
            using var _ = h;
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("a@test");
            await h.AddVolunteerAsync("b@test");
            await h.AddVolunteerAsync("c@test");
            var d = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");

            // כל עוד יש הצעה פתוחה - אין הצעה נוספת לאותה תרומה, גם לא בהרצה נוספת של המנוע
            await h.Engine.RunBatchAsync("Manual");
            Assert.False((await h.Engine.MatchDonationAsync(d.Id, "Manual"))!.Matched);
            h.Fresh();
            Assert.Single(await h.Db.Matches.Where(m => m.IsActive).ToListAsync());

            // דחייה: התרומה עוברת מיד (בלי לחכות לסוף 5 הדקות) - ושוב רק הצעה פתוחה אחת
            var first = await h.Db.Matches.SingleAsync(m => m.IsActive);
            var firstUser = await h.Db.Users.SingleAsync(u => u.VolunteerProfile != null && u.VolunteerProfile.Id == first.VolunteerId);
            Assert.Equal(RouteResponseResult.Done, await h.MatchService.DeclineAsync(first.Id, As(firstUser)));
            Assert.True((await h.Engine.MatchDonationAsync(d.Id, "Reassign"))!.Matched);

            h.Fresh();
            var open = Assert.Single(await h.Db.Matches.Where(m => m.IsActive).ToListAsync());
            Assert.NotEqual(first.VolunteerId, open.VolunteerId);
            Assert.Equal(MatchStatus.AwaitingVolunteerConfirmation, open.Status);
        }
    }
}
