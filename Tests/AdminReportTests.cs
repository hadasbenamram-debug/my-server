using BLL.Services;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // תיקון 17: "מובילי ביצועי מתנדבים" ושאר המספרים בדוח המנהל מחושבים מהנתונים בפועל
    public class AdminReportTests
    {
        // GetSystemReportAsync לא משתמש בבקשות ההצטרפות - לכן null
        private static AdminReportService Reports(EngineHarness h) =>
            new(h.Volunteers, null!, h.Donations, h.Matches, TestDb.Mapper);

        private static CurrentUserDto As(User user) => new() { UserId = user.Id };

        private static async Task<Match> AddMatchAsync(EngineHarness h, Volunteer volunteer, Donation donation, MatchStatus status,
            DateTime? assignedAt = null, TimeSpan? respondedAfter = null, int? routeId = null)
        {
            var assigned = assignedAt ?? h.NowUtc.AddHours(-2);
            var match = new Match
            {
                DonationId = donation.Id, NgoId = h.Ngo.Id, VolunteerId = volunteer.Id, VolunteerRouteId = routeId,
                Status = status, IsActive = false, AssignedAt = assigned,
                RespondedAt = respondedAfter.HasValue ? assigned + respondedAfter.Value : null,
                DeliveredAt = status == MatchStatus.Completed ? assigned.AddHours(1) : null
            };
            h.Db.Matches.Add(match);
            await h.Db.SaveChangesAsync();
            return match;
        }

        private static async Task<Donation> AddDonationAsync(EngineHarness h, DonationStatus status, double weightKg = 5)
        {
            var donation = await h.AddDonationAsync(weightKg);
            donation.Status = status;
            await h.Db.SaveChangesAsync();
            return donation;
        }

        [Fact]
        public async Task AfterASuccessfulDelivery_TheVolunteerIsListedWithOneDelivery()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("dana@test");
            await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var matchId = (await h.Db.Matches.SingleAsync()).Id;
            var user = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);

            var route = (await h.MatchService.AcceptAsync(matchId, As(user))).Route!;
            await h.MatchService.StartRouteAsync(route.Id, As(user));
            await h.MatchService.PickUpAsync(matchId, As(user));
            await h.MatchService.CompleteAsync(matchId, As(user));
            h.Fresh();

            var report = await Reports(h).GetSystemReportAsync();

            // בעבר: DeliveriesCount היה תמיד 0
            var top = Assert.Single(report.TopVolunteers);
            Assert.Equal("dana", top.Name);
            Assert.Equal(1, top.DeliveriesCount);
        }

        [Fact]
        public async Task TopVolunteers_AreRankedByCompletedDeliveries_AndVolunteersWithoutDeliveriesAreNotListed()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            // ציון האמינות הגבוה ביותר - אבל בלי אף משלוח שהושלם (בעבר הוא היה ראשון ברשימה)
            var reliable = await h.AddVolunteerAsync("reliable@test", reliability: 5);
            var one = await h.AddVolunteerAsync("one@test", reliability: 2);
            var three = await h.AddVolunteerAsync("three@test", reliability: 1);

            for (int i = 0; i < 3; i++)
                await AddMatchAsync(h, three, await AddDonationAsync(h, DonationStatus.Completed), MatchStatus.Completed);
            await AddMatchAsync(h, one, await AddDonationAsync(h, DonationStatus.Delivered), MatchStatus.Completed);

            // לא משלוחים מוצלחים: דחייה, אי-מענה, No-Show
            await AddMatchAsync(h, one, await AddDonationAsync(h, DonationStatus.Pending), MatchStatus.Declined);
            await AddMatchAsync(h, reliable, await AddDonationAsync(h, DonationStatus.Pending), MatchStatus.TimedOut);
            await AddMatchAsync(h, reliable, await AddDonationAsync(h, DonationStatus.Pending), MatchStatus.NoShow);
            h.Fresh();

            var report = await Reports(h).GetSystemReportAsync();

            Assert.Equal(new[] { ("three", 3), ("one", 1) },
                report.TopVolunteers.Select(t => (t.Name, t.DeliveriesCount)).ToArray());
        }

        [Fact]
        public async Task ResponseTime_SuccessRate_AndDeliveredKg_AreComputedFromTheData()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");

            // נמסרו: 10 + 5.2 + 4 ק"ג. פג תוקף: 100 ק"ג (לא נספר במשקל). בוטלה / ממתינה: לא נספרות באחוז ההצלחה
            var completed1 = await AddDonationAsync(h, DonationStatus.Completed, 10);
            var completed2 = await AddDonationAsync(h, DonationStatus.Completed, 5.2);
            var delivered = await AddDonationAsync(h, DonationStatus.Delivered, 4);
            await AddDonationAsync(h, DonationStatus.Expired, 100);
            await AddDonationAsync(h, DonationStatus.Cancelled, 7);
            var pending = await AddDonationAsync(h, DonationStatus.Pending, 3);

            // זמני תגובה: הצעה אחת נענתה אחרי 2 דקות; הצעה של שתי תחנות (אותו מסלול) נענתה פעם אחת אחרי 4 דקות;
            // הצעה שלא נענתה בזמן לא נספרת. ממוצע לפי הצעות: (2 + 4) / 2 = 3
            var at = h.NowUtc.AddHours(-3);
            await AddMatchAsync(h, v, completed1, MatchStatus.Completed, at, TimeSpan.FromMinutes(2));
            await AddMatchAsync(h, v, completed2, MatchStatus.Completed, at, TimeSpan.FromMinutes(4), routeId: 40);
            await AddMatchAsync(h, v, delivered, MatchStatus.Completed, at, TimeSpan.FromMinutes(4), routeId: 40);
            await AddMatchAsync(h, v, pending, MatchStatus.TimedOut, at);
            h.Fresh();

            var report = await Reports(h).GetSystemReportAsync();

            Assert.Equal("3.0", report.AverageResponseTime);
            Assert.Equal("75%", report.SuccessRate);          // 3 נמסרו מתוך 4 שהסתיימו
            Assert.Equal(19, report.TotalDonationsVolume);    // 19.2 ק"ג
        }

        [Fact]
        public async Task WithNoData_TheReportShowsADashInsteadOfMadeUpNumbers()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");

            var report = await Reports(h).GetSystemReportAsync();

            Assert.Equal("—", report.AverageResponseTime);
            Assert.Equal("—", report.SuccessRate);
            Assert.Equal(0, report.TotalDonationsVolume);
            Assert.Empty(report.TopVolunteers);
        }
    }
}
