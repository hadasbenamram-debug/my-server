using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // תיקון 16: היסטוריית השיבוצים של המתנדב מתעדכנת אחרי התנדבות
    public class VolunteerHistoryTests
    {
        private static CurrentUserDto As(User user) => new() { UserId = user.Id, IsAdmin = user.Role == UserRole.Admin };

        [Fact]
        public async Task ACompletedDelivery_AppearsInTheHistory_ButNotInTheActiveList()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var matchId = (await h.Db.Matches.SingleAsync()).Id;
            var user = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);

            // הצעה שעדיין ממתינה לתשובה - ב"בקשה נכנסת", לא בהיסטוריה
            Assert.Empty(await h.MatchService.GetMyVolunteerHistoryAsync(As(user)));

            var route = (await h.MatchService.AcceptAsync(matchId, As(user))).Route!;
            await h.MatchService.StartRouteAsync(route.Id, As(user));
            await h.MatchService.PickUpAsync(matchId, As(user));
            await h.MatchService.CompleteAsync(matchId, As(user));
            h.Fresh();

            // בעבר ההיסטוריה הגיעה מ-matches/active, ושם שיבוץ שהסתיים כבר לא מופיע
            Assert.Empty(await h.MatchService.GetActiveAsync(As(user)));

            var history = Assert.Single(await h.MatchService.GetMyVolunteerHistoryAsync(As(user)));
            Assert.Equal(matchId, history.Id);
            Assert.Equal(nameof(MatchStatus.Completed), history.StatusName.ToString());
            Assert.NotNull(history.DeliveredAt);
            Assert.Equal(h.Ngo.NgoName, history.NgoName);
        }

        [Fact]
        public async Task DeclinedRequests_StayInTheHistory_AndOnlyTheVolunteersOwnMatchesAreShown()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            var user = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);
            await h.MatchService.DeclineAsync(match.Id, As(user));

            var history = Assert.Single(await h.MatchService.GetMyVolunteerHistoryAsync(As(user)));
            Assert.Equal(nameof(MatchStatus.Declined), history.StatusName.ToString());

            // ספק (שאין לו פרופיל מתנדב) - היסטוריה ריקה, גם כשיש לו תרומות עם שיבוצים
            var supplierUser = await h.Db.Users.SingleAsync(u => u.Email == "supplier@test");
            Assert.Empty(await h.MatchService.GetMyVolunteerHistoryAsync(As(supplierUser)));
        }
    }
}
