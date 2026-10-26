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
    }
}
