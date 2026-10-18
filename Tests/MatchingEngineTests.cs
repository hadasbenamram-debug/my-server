using BLL.Algorithms;
using BLL.Exceptions;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // בדיקות רגרסיה למנוע השידוכים ולמחזור החיים של מסלול - על מסד בזיכרון עם כל השכבות האמיתיות
    public class MatchingEngineTests
    {
        private static CurrentUserDto As(User user) => new() { UserId = user.Id, IsAdmin = user.Role == UserRole.Admin };

        private static async Task<(EngineHarness H, Volunteer V, Donation D)> SetupAsync()
        {
            var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            var d = await h.AddDonationAsync();
            return (h, v, d);
        }

        // ---------------- run-engine שומר בפועל + ResponseDeadline ----------------

        [Fact]
        public async Task RunEngine_SavesRouteAndMatch_WithResponseDeadline()
        {
            var (h, v, d) = await SetupAsync();
            using var _ = h;

            var result = await h.MatchService.RunEngineAsync();

            Assert.True(result.Started);
            Assert.Equal(1, result.RoutesCreated);
            Assert.Equal(1, result.MatchedCount);

            h.Fresh();
            var match = Assert.Single(await h.Db.Matches.ToListAsync());
            Assert.Equal(d.Id, match.DonationId);
            Assert.Equal(v.Id, match.VolunteerId);
            Assert.Equal(h.Ngo.Id, match.NgoId);
            Assert.Equal(MatchStatus.AwaitingVolunteerConfirmation, match.Status);
            Assert.True(match.IsActive);
            Assert.NotNull(match.ResponseDeadline);
            Assert.True(match.ResponseDeadline > match.AssignedAt, "ResponseDeadline must be after AssignedAt.");
            Assert.NotNull(match.VolunteerRouteId);
            Assert.True(match.MatchScore > 0);

            var route = await h.Db.VolunteerRoutes.SingleAsync();
            Assert.Equal(RouteStatus.Planned, route.Status);

            Assert.Equal(DonationStatus.Matched, (await h.Db.Donations.SingleAsync()).Status);

            // בדיקת ה-timeout מתוזמנת לרגע שבו הזמן נגמר
            Assert.True(h.Jobs.Has<MatchTimeoutService>(nameof(MatchTimeoutService.ProcessTimeoutsAsync)));
        }

        [Fact]
        public async Task EngineStatus_ReflectsTheLastRealRun()
        {
            var (h, _, _) = await SetupAsync();
            using var __ = h;

            var before = await h.MatchService.GetEngineStatusAsync();
            Assert.Null(before.LastRunAt);
            Assert.Equal(1, before.PendingDonations);

            await h.MatchService.RunEngineAsync();
            var after = await h.MatchService.GetEngineStatusAsync();

            Assert.NotNull(after.LastRunAt);
            Assert.Equal("Manual", after.LastRunTrigger);
            Assert.Equal(1, after.RoutesCreated);
            Assert.Equal(0, after.PendingDonations);
            Assert.Equal(1, after.AwaitingVolunteerResponse);
        }

        // ---------------- קישור המייל: טוקן חד-פעמי למסלול, לא JWT ----------------

        [Fact]
        public async Task VolunteerEmail_IsQueuedOnce_EncryptedAtRest_WithSingleUseRouteLink()
        {
            var (h, v, _) = await SetupAsync();
            using var _ = h;

            await h.MatchService.RunEngineAsync();
            h.Fresh();

            var emails = await h.Db.Notifications
                .Where(n => n.UserId == v.UserId && n.Channel == NotificationChannel.Email).ToListAsync();
            var email = Assert.Single(emails);

            // הגוף נשמר מוצפן; בטבלה אין קישור/טוקן בטקסט גלוי
            Assert.NotNull(email.ProtectedPayload);
            Assert.DoesNotContain("token=", email.ProtectedPayload!);
            Assert.DoesNotContain("token=", email.Content ?? "");
            var body = h.Protector.Unprotect(email.ProtectedPayload!);
            Assert.Contains("respond?token=", body);

            var rawToken = Uri.UnescapeDataString(body.Split("token=")[1].Split('"', '&', '<', '\'', ' ')[0]);
            Assert.DoesNotContain(".", rawToken);   // לא JWT

            var route = await h.MatchService.GetRouteByLinkAsync(rawToken);
            Assert.NotNull(route);

            var answered = await h.MatchService.RespondByLinkAsync(new RespondByLinkDto { Token = rawToken, Accept = true });
            Assert.Equal(nameof(MatchStatus.Confirmed), answered.Stops.Single().Status);

            // הטוקן נוצל - אי אפשר להשתמש בו שוב
            Assert.Null(await h.MatchService.GetRouteByLinkAsync(rawToken));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                h.MatchService.RespondByLinkAsync(new RespondByLinkDto { Token = rawToken, Accept = false }));
        }

        // ---------------- ApprovalStatus ----------------

        [Fact]
        public async Task UnapprovedVolunteer_IsNeverMatched_AndAdminIsNotified()
        {
            var h = new EngineHarness();
            using var _ = h;
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("pending@test", ApprovalStatus.Pending);
            await h.AddVolunteerAsync("rejected@test", ApprovalStatus.Rejected);
            await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
            h.Fresh();
            Assert.Empty(await h.Db.Matches.ToListAsync());

            // תוקן: התראת המנהל נכשלה כי ExternalMessageId היה חובה והחריגה נבלעה
            var adminNotes = await h.Db.Notifications.Where(n => n.UserId == h.AdminUser.Id).ToListAsync();
            Assert.Contains(adminNotes, n => n.Channel == NotificationChannel.InApp);
            Assert.Contains(adminNotes, n => n.Channel == NotificationChannel.Email && n.ExternalMessageId == null);
        }

        [Fact]
        public async Task UnapprovedNgo_DoesNotReceiveDonations()
        {
            var h = new EngineHarness();
            using var _ = h;
            await h.SeedBasicsAsync();
            var ngo = await h.Db.Ngos.SingleAsync();
            ngo.ApprovalStatus = ApprovalStatus.Pending;
            await h.Db.SaveChangesAsync();
            await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
            Assert.Contains(result.FailedConstraints, f => f.Contains("עמותה"));
        }

        [Fact]
        public async Task VolunteerWithExpiredLicense_IsNotMatched()
        {
            var h = new EngineHarness();
            using var _ = h;
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("expired@test", licenseExpiry: h.NowUtc.AddDays(-1));
            await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
        }

        // ---------------- שיבוץ ידני: שידוך אחד, מייל אחד ----------------

        [Fact]
        public async Task ManualAssign_CreatesExactlyOneMatch_AndOneEmail()
        {
            var (h, v, d) = await SetupAsync();
            using var _ = h;

            var first = await h.MatchService.AssignDonationAsync(d.Id);

            Assert.NotNull(first);
            Assert.True(first!.Matched);
            Assert.NotNull(first.MatchId);
            Assert.NotNull(first.RouteId);

            var second = await h.MatchService.AssignDonationAsync(d.Id);
            Assert.False(second!.Matched);

            h.Fresh();
            Assert.Single(await h.Db.Matches.ToListAsync());
            Assert.Single(await h.Db.Notifications
                .Where(n => n.UserId == v.UserId && n.Channel == NotificationChannel.Email).ToListAsync());
        }

        [Fact]
        public async Task ManualAssign_UnknownDonation_ReturnsNull()
        {
            var (h, _, _) = await SetupAsync();
            using var __ = h;
            Assert.Null(await h.MatchService.AssignDonationAsync(9999));
        }

        // ---------------- Timeout לפי השעה הנוכחית ----------------

        [Fact]
        public async Task Timeout_UsesTheCurrentTime_ReleasesDonation_AndRequeues()
        {
            var (h, _, d) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var deadline = (await h.Db.Matches.SingleAsync()).ResponseDeadline!.Value;

            // לפני המועד - כלום לא קורה
            Assert.Equal(0, await h.Timeouts.ProcessTimeoutsAtAsync(deadline.AddMinutes(-1)));

            // אחרי המועד - השידוך פג, התרומה חוזרת לממתינות ונשלחת לשיבוץ מחדש מיידי
            Assert.Equal(1, await h.Timeouts.ProcessTimeoutsAtAsync(deadline.AddMinutes(1)));

            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.TimedOut, match.Status);
            Assert.False(match.IsActive);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync()).Status);
            Assert.Equal(RouteStatus.Cancelled, (await h.Db.VolunteerRoutes.SingleAsync()).Status);
            Assert.True(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
        }

        // ---------------- Decline / Complete מעדכנים את התרומה ----------------

        [Fact]
        public async Task Decline_ReturnsDonationToPending_AndTheSameVolunteerIsNotOfferedAgain()
        {
            var (h, v, d) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            var volunteerUser = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);

            await h.MatchService.DeclineAsync(match.Id, As(volunteerUser));

            h.Fresh();
            match = await h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.Declined, match.Status);
            Assert.False(match.IsActive);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync()).Status);
            Assert.True(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));

            // שיבוץ מחדש: המתנדב שדחה לא מקבל את אותה תרומה שוב
            var again = await h.Engine.MatchDonationAsync(d.Id, "Reassign");
            Assert.False(again!.Matched);
        }

        [Fact]
        public async Task Decline_ByAnotherVolunteer_LooksLikeNotFound()
        {
            var (h, _, _) = await SetupAsync();
            using var _ = h;
            var other = await h.AddVolunteerAsync("other@test", withSlot: false);
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            var otherUser = await h.Db.Users.SingleAsync(u => u.Id == other.UserId);

            await Assert.ThrowsAsync<NotFoundException>(() => h.MatchService.DeclineAsync(match.Id, As(otherUser)));
        }

        [Fact]
        public async Task FullLifecycle_Accept_Start_PickUp_Deliver_ConfirmReceipt()
        {
            var (h, v, d) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var matchId = (await h.Db.Matches.SingleAsync()).Id;
            var volunteerUser = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);

            // אישור אחד לכל המסלול
            var route = await h.MatchService.AcceptAsync(matchId, As(volunteerUser));
            h.Fresh();
            Assert.Equal(MatchStatus.Confirmed, (await h.Db.Matches.SingleAsync()).Status);
            Assert.False((await h.Db.Volunteers.SingleAsync(x => x.Id == v.Id)).IsAvailable);

            await h.MatchService.StartRouteAsync(route.Id, As(volunteerUser));
            h.Fresh();
            Assert.Equal(MatchStatus.InPickup, (await h.Db.Matches.SingleAsync()).Status);
            Assert.Equal(RouteStatus.InProgress, (await h.Db.VolunteerRoutes.SingleAsync()).Status);

            await h.MatchService.PickUpAsync(matchId, As(volunteerUser));
            h.Fresh();
            var pickedUp = await h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.InTransit, pickedUp.Status);
            Assert.NotNull(pickedUp.PickedUpAt);
            Assert.Equal(DonationStatus.PickedUp, (await h.Db.Donations.SingleAsync()).Status);

            // תוקן: "complete" לא עדכן את התרומה (match.Donation לא נטען) ו-IsActive נשאר true
            await h.MatchService.CompleteAsync(matchId, As(volunteerUser));
            h.Fresh();
            var delivered = await h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.Completed, delivered.Status);
            Assert.False(delivered.IsActive);
            Assert.NotNull(delivered.DeliveredAt);
            Assert.Equal(DonationStatus.Delivered, (await h.Db.Donations.SingleAsync()).Status);
            Assert.Equal(RouteStatus.Completed, (await h.Db.VolunteerRoutes.SingleAsync()).Status);
            Assert.True((await h.Db.Volunteers.SingleAsync(x => x.Id == v.Id)).IsAvailable);

            // העמותה מאשרת קבלה
            var confirmed = await h.Workflow.ConfirmReceiptAsync(d.Id, h.Ngo.Id);
            h.Fresh();
            var donation = await h.Db.Donations.SingleAsync();
            Assert.Equal(DonationStatus.Completed, donation.Status);
            Assert.NotNull(donation.ReceivedAt);
            Assert.Equal(matchId, confirmed.Id);
        }

        [Fact]
        public async Task ConfirmReceipt_BeforePickup_IsRejected()
        {
            var (h, _, d) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");

            await Assert.ThrowsAsync<ArgumentException>(() => h.Workflow.ConfirmReceiptAsync(d.Id, h.Ngo.Id));
        }

        // ---------------- No-Show ----------------

        [Fact]
        public async Task NoShow_RequeuesDonation_AndLowersReliability()
        {
            var (h, v, _) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var matchId = (await h.Db.Matches.SingleAsync()).Id;
            var volunteerUser = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);
            await h.MatchService.AcceptAsync(matchId, As(volunteerUser));

            var requeued = await h.MatchService.MarkNoShowAsync(matchId, "לא הגיע");

            Assert.Equal(1, requeued);
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.NoShow, match.Status);
            Assert.False(match.IsActive);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync()).Status);
            Assert.True((await h.Db.Volunteers.SingleAsync(x => x.Id == v.Id)).ReliabilityScore < 3.5);
            Assert.True(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
        }

        [Fact]
        public async Task AutoNoShow_AfterGracePeriod_WithoutPickup()
        {
            var (h, v, _) = await SetupAsync();
            using var _ = h;
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            var volunteerUser = await h.Db.Users.SingleAsync(u => u.Id == v.UserId);
            await h.MatchService.AcceptAsync(match.Id, As(volunteerUser));

            var eta = match.EstimatedArrival!.Value;
            Assert.Equal(0, await h.Timeouts.ProcessNoShowsAtAsync(eta.AddMinutes(10)));
            Assert.Equal(1, await h.Timeouts.ProcessNoShowsAtAsync(eta.AddMinutes(31)));

            h.Fresh();
            Assert.Equal(MatchStatus.NoShow, (await h.Db.Matches.SingleAsync()).Status);
        }

        // ---------------- Matching Score ----------------

        [Fact]
        public void ScoreRoutes_PrefersBetterVolunteerScore_AndEfficientRoutes()
        {
            var fast = new RouteCandidate
            {
                Candidate = new VolunteerCandidate(new Volunteer { Id = 1 }, 1, score: 0.9),
                Plan = new RoutePlan { KnapsackValue = 10, TotalDurationMinutes = 20 }
            };
            var slow = new RouteCandidate
            {
                Candidate = new VolunteerCandidate(new Volunteer { Id = 2 }, 5, score: 0.5),
                Plan = new RoutePlan { KnapsackValue = 10, TotalDurationMinutes = 120 }
            };

            DonationMatchingBatchService.ScoreRoutes(new[] { fast, slow });

            Assert.True(fast.Score > slow.Score);
            Assert.InRange(fast.Score, 0, 100);
            Assert.InRange(slow.Score, 0, 100);
        }
    }
}
