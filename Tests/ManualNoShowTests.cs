using BLL.Algorithms;
using BLL.Configuration;
using BLL.Notifications;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using DonationStatus = DAL.Classes.DonationStatus;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // תיקון 36: טיפול ידני של המנהל באי-הגעה: סימון בלי שיבוץ אוטומטי, לוח "מי מאחר", ושיבוץ ידני למתנדב שהמנהל בוחר
    public sealed class ManualNoShowTests
    {
        private static CurrentUserDto As(int userId) => new() { UserId = userId };

        // מתנדב, תרומה, ריצת מנוע ואישור המסלול
        private static async Task<(Volunteer Volunteer, int MatchId, int DonationId)> AcceptedRouteAsync(EngineHarness h)
        {
            await h.SeedBasicsAsync();
            var volunteer = await h.AddVolunteerAsync("late@test");
            var donation = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();

            var matchId = (await h.Db.Matches.SingleAsync()).Id;
            await h.MatchService.AcceptAsync(matchId, As(volunteer.UserId));
            h.Fresh();
            return (volunteer, matchId, donation.Id);
        }

        // ---------------- סימון No-Show ----------------

        [Fact]
        public async Task NoShow_WithAutomaticReassignment_SchedulesTheEngine_AsBefore()
        {
            using var h = new EngineHarness();
            var (_, matchId, _) = await AcceptedRouteAsync(h);

            await h.MatchService.MarkNoShowAsync(matchId, "לא הגיע");

            Assert.True(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
        }

        [Fact]
        public async Task NoShow_HandledManually_LeavesTheDonationPending_WithoutSchedulingTheEngine()
        {
            using var h = new EngineHarness();
            var (_, matchId, donationId) = await AcceptedRouteAsync(h);

            var count = await h.MatchService.MarkNoShowAsync(matchId, "לא הגיע", autoReassign: false);
            h.Fresh();

            Assert.Equal(1, count);
            Assert.False(h.Jobs.Has<DonationMatchingBatchService>(nameof(DonationMatchingBatchService.ReassignDonationAsync)));
            Assert.Equal(MatchStatus.NoShow, (await h.Db.Matches.SingleAsync()).Status);
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync(d => d.Id == donationId)).Status);
            Assert.Contains(await h.Db.Notifications.ToListAsync(),
                n => n.UserId == h.AdminUser.Id && n.Content != null && n.Content.Contains("שיבוץ ידני"));
        }

        // ---------------- לוח "מי מאחר" ----------------

        [Fact]
        public async Task Board_ShowsLateRoutesFirst_WithTheVolunteersPhone()
        {
            using var h = new EngineHarness();
            var (volunteer, matchId, _) = await AcceptedRouteAsync(h);

            // תיקון 42: הלוח מחשב איחור לפי השעון של המערכת (בבדיקות - השעון הקבוע): זמן ההגעה המשוער היה לפני 20 דקות
            var match = await h.Db.Matches.SingleAsync(m => m.Id == matchId);
            match.EstimatedArrival = h.NowUtc.AddMinutes(-20);
            await h.Db.SaveChangesAsync();
            h.Fresh();

            var board = await h.MatchService.GetNoShowBoardAsync();

            var late = Assert.Single(board.AwaitingPickup);
            Assert.Equal(matchId, late.MatchId);
            Assert.Equal(volunteer.Id, late.VolunteerId);
            Assert.Equal("0501234567", late.VolunteerPhone);
            Assert.InRange(late.MinutesLate, 19, 21);
            Assert.False(late.Started);
            Assert.Single(late.Stops);
            Assert.Empty(board.NoShows);
        }

        [Fact]
        public async Task Board_AfterANoShow_ShowsWhatHappenedToTheDonation()
        {
            using var h = new EngineHarness();
            var (_, matchId, donationId) = await AcceptedRouteAsync(h);
            await h.MatchService.MarkNoShowAsync(matchId, "לא הגיע", autoReassign: false);
            h.Fresh();

            var board = await h.MatchService.GetNoShowBoardAsync();

            Assert.Empty(board.AwaitingPickup);
            var noShow = Assert.Single(board.NoShows);
            Assert.Equal(donationId, noShow.DonationId);
            Assert.Equal("Pending", noShow.DonationStatus);
            Assert.Null(noShow.ReassignedToVolunteerName);
            Assert.Equal("0501234567", noShow.VolunteerPhone);
        }

        [Fact]
        public async Task Board_ADonationFromToday_CanBeAssigned()
        {
            // תיקון 42: "אפשר לשבץ" נבדק מול השעון של המערכת - השעון הקבוע של הבדיקה (יום שני 10:00).
            // בעבר הבדיקה רצה על השעון האמיתי ו"עברה" בלי לבדוק כלום בלילה ובשבת.
            using var h = new EngineHarness(new MatchingPolicyOptions { AutoNoShowGraceMinutes = 30, ObserveShabbat = false });
            var (_, matchId, _) = await AcceptedRouteAsync(h);
            await h.MatchService.MarkNoShowAsync(matchId, null, autoReassign: false);
            h.Fresh();

            Assert.True(Assert.Single((await h.MatchService.GetNoShowBoardAsync()).NoShows).CanAssign);
        }

        // ---------------- המתנדבים לשיבוץ ידני ----------------

        [Fact]
        public async Task Candidates_PutAssignableVolunteersWithAShiftFirst_AndExplainTheOthers()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var donation = await h.AddDonationAsync(weightKg: 20);
            var onShift = await h.AddVolunteerAsync("on-shift@test");
            var noShift = await h.AddVolunteerAsync("no-shift@test", withSlot: false);
            var smallCar = await h.AddVolunteerAsync("small-car@test", capacityKg: 5);

            var options = (await h.MatchService.GetManualAssignmentOptionsAsync(donation.Id))!;

            Assert.Null(options.Problem);
            Assert.Equal(20, options.WeightKg, 3);
            Assert.Equal(h.Supplier.BusinessName, options.SupplierName);
            Assert.Equal(new[] { onShift.Id, noShift.Id, smallCar.Id }, options.Volunteers.Select(v => v.VolunteerId).ToArray());

            var first = options.Volunteers[0];
            Assert.True(first.CanAssign);
            Assert.True(first.AvailableBySchedule);
            Assert.True(first.HasFreshLocation);
            Assert.NotNull(first.DistanceKm);
            Assert.Equal("0501234567", first.Phone);

            Assert.True(options.Volunteers[1].CanAssign);
            Assert.False(options.Volunteers[1].AvailableBySchedule);
            Assert.Contains("בתיאום", options.Volunteers[1].Note);

            Assert.False(options.Volunteers[2].CanAssign);
            Assert.Contains("קיבולת", options.Volunteers[2].Note);
        }

        [Fact]
        public async Task Candidates_ForADonationThatIsNotWaiting_ExplainWhy()
        {
            using var h = new EngineHarness();
            var (_, _, donationId) = await AcceptedRouteAsync(h);      // התרומה כבר שובצה

            var options = (await h.MatchService.GetManualAssignmentOptionsAsync(donationId))!;

            Assert.Equal("התרומה אינה ממתינה לשיבוץ.", options.Problem);
            Assert.Empty(options.Volunteers);
            Assert.Null(await h.MatchService.GetManualAssignmentOptionsAsync(9999));
        }

        // ---------------- שיבוץ ידני ----------------

        [Fact]
        public async Task Assign_SendsARegularOfferToTheChosenVolunteer()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var donation = await h.AddDonationAsync();
            await h.AddVolunteerAsync("engine-choice@test");
            var chosen = await h.AddVolunteerAsync("admin-choice@test", reliability: 2.0);

            var result = await h.MatchService.AssignDonationToVolunteerAsync(donation.Id, chosen.Id, ignoreSchedule: false);
            h.Fresh();

            Assert.True(result.Matched, result.Message);
            Assert.Contains("admin-choice", result.Message);
            var match = await h.Db.Matches.SingleAsync();
            Assert.Equal(chosen.Id, match.VolunteerId);
            Assert.Equal(h.Ngo.Id, match.NgoId);
            Assert.Equal(MatchStatus.AwaitingVolunteerConfirmation, match.Status);
            Assert.NotNull(match.ResponseDeadline);
            Assert.Equal(DonationStatus.Matched, (await h.Db.Donations.SingleAsync()).Status);
            Assert.Contains(await h.Db.Notifications.ToListAsync(),
                n => n.UserId == chosen.UserId && n.Channel == NotificationChannel.Email);
        }

        [Fact]
        public async Task Assign_OutsideTheVolunteersShift_NeedsTheAdminToCoordinate()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var donation = await h.AddDonationAsync();
            var noShift = await h.AddVolunteerAsync("no-shift@test", withSlot: false);

            var refused = await h.MatchService.AssignDonationToVolunteerAsync(donation.Id, noShift.Id, ignoreSchedule: false);
            Assert.False(refused.Matched);
            Assert.Contains("תיאמתי עם המתנדב", refused.Message);
            Assert.Empty(await h.Db.Matches.ToListAsync());

            var coordinated = await h.MatchService.AssignDonationToVolunteerAsync(donation.Id, noShift.Id, ignoreSchedule: true);
            h.Fresh();
            Assert.True(coordinated.Matched, coordinated.Message);
            Assert.Equal(noShift.Id, (await h.Db.Matches.SingleAsync()).VolunteerId);
        }

        [Fact]
        public async Task Assign_KeepsTheHardConstraints()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var heavy = await h.AddDonationAsync(weightKg: 20);
            var light = await h.AddDonationAsync(weightKg: 2);
            var smallCar = await h.AddVolunteerAsync("small-car@test", capacityKg: 5);
            var busy = await h.AddVolunteerAsync("busy@test");
            Assert.True((await h.MatchService.AssignDonationToVolunteerAsync(light.Id, busy.Id, ignoreSchedule: false)).Matched);

            // גם בתיאום עם המתנדב: קיבולת הרכב, ומתנדב שכבר יש לו הצעה / מסלול - לא עוקפים
            var tooSmall = await h.MatchService.AssignDonationToVolunteerAsync(heavy.Id, smallCar.Id, ignoreSchedule: true);
            Assert.False(tooSmall.Matched);
            Assert.Contains("קיבולת", tooSmall.Message);

            var occupied = await h.MatchService.AssignDonationToVolunteerAsync(heavy.Id, busy.Id, ignoreSchedule: true);
            Assert.False(occupied.Matched);
            Assert.Contains("מסלול פעיל", occupied.Message);

            h.Fresh();
            Assert.Equal(DonationStatus.Pending, (await h.Db.Donations.SingleAsync(d => d.Id == heavy.Id)).Status);
            Assert.Single(await h.Db.Matches.ToListAsync());
        }

        [Fact]
        public async Task Assign_TheVolunteerWhoDidNotShowUp_IsAllowed_WhenTheAdminDecides()
        {
            using var h = new EngineHarness();
            var (volunteer, matchId, donationId) = await AcceptedRouteAsync(h);
            await h.MatchService.MarkNoShowAsync(matchId, "התקשר - מאחר בחצי שעה", autoReassign: false);
            h.Fresh();

            var options = (await h.MatchService.GetManualAssignmentOptionsAsync(donationId))!;
            var same = Assert.Single(options.Volunteers, v => v.VolunteerId == volunteer.Id);
            Assert.True(same.FailedThisDonationBefore);
            Assert.True(same.CanAssign);

            var result = await h.MatchService.AssignDonationToVolunteerAsync(donationId, volunteer.Id, ignoreSchedule: false);
            Assert.True(result.Matched, result.Message);
        }

        // ---------------- תיקון 66: הסיבה המדויקת כשאין מסלול ----------------

        [Fact]
        public async Task Assign_NoRoute_TellsThatTheNgoIsClosed_AndWhenItOpens()
        {
            // יום שני 15:40. העמותה מקבלת עד 15:47 - אפשר היה להגיע "על הנייר", אבל אחרי 5 דקות לתגובה,
            // איסוף ונסיעה המתנדב מגיע אחרי הסגירה. התרומה צריכה להיאסף עד 16:40, ולכן אי אפשר לחכות למחר.
            using var h = new EngineHarness(localNow: new DateTime(2026, 10, 19, 15, 40, 0));
            await h.SeedBasicsAsync();
            h.Ngo.SunToThuOpenTime = new TimeSpan(8, 0, 0);
            h.Ngo.SunToThuCloseTime = new TimeSpan(15, 47, 0);
            await h.Db.SaveChangesAsync();
            // תפוגה רחוקה: העמותה עוברת את הסינון המוקדם (פתוחה מחר לפני התפוגה) - הבעיה רק בשעת האיסוף האחרונה
            var donation = await h.AddDonationAsync(pickupIn: TimeSpan.FromHours(1), expiresIn: TimeSpan.FromHours(24));
            var volunteer = await h.AddVolunteerAsync("closed-ngo@test");

            var result = await h.MatchService.AssignDonationToVolunteerAsync(donation.Id, volunteer.Id, ignoreSchedule: true);

            Assert.False(result.Matched);
            Assert.Contains("עמותת בדיקה", result.Message);
            Assert.Contains("סגורה", result.Message);
            Assert.Contains("08:00 (מחר)", result.Message);
            Assert.Contains("16:40", result.Message);
            Assert.Empty(await h.Db.Matches.ToListAsync());
        }

        [Fact]
        public async Task Assign_NoRoute_TellsThatTheVolunteerArrivesAfterTheLatestPickup()
        {
            // המתנדב רחוק (כ-25 ק"מ) והתרומה צריכה להיאסף בעוד 20 דקות
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var donation = await h.AddDonationAsync(pickupIn: TimeSpan.FromMinutes(20));
            var volunteer = await h.AddVolunteerAsync("far-away@test");
            volunteer.CurrentLatitude = 31.85;
            volunteer.CurrentLongitude = 34.70;
            await h.Db.SaveChangesAsync();
            h.Fresh();

            var result = await h.MatchService.AssignDonationToVolunteerAsync(donation.Id, volunteer.Id, ignoreSchedule: true);

            Assert.False(result.Matched);
            Assert.Contains("לנקודת האיסוף", result.Message);
            Assert.Contains("10:20", result.Message);   // שעת האיסוף האחרונה
        }
    }
}
