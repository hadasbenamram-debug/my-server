using BLL.Algorithms;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 9: אין זמינות ואין שיבוצים בשבת - רק במוצאי שבת
    public class ShabbatTests
    {
        private static readonly MatchingPolicyOptions Policy = new();   // ברירת מחדל: ירושלים, 40 דקות / 40 דקות

        static ShabbatTests() => LocalClock.Configure("Asia/Jerusalem");

        private static DateTime Utc(int y, int mo, int d, int h, int mi) => LocalClock.ToUtc(new DateTime(y, mo, d, h, mi, 0));

        private static void AssertClose(DateTime expectedLocal, DateTime actualUtc, double toleranceMinutes = 1.5)
        {
            var actualLocal = LocalClock.ToLocal(actualUtc);
            Assert.True(Math.Abs((actualLocal - expectedLocal).TotalMinutes) <= toleranceMinutes,
                $"expected ~{expectedLocal:yyyy-MM-dd HH:mm}, got {actualLocal:yyyy-MM-dd HH:mm:ss}");
        }

        // ---------------- ShabbatClock ----------------

        // השקיעה בירושלים (בגובה פני הים). ערכי הייחוס חושבו בנפרד (ספריית astral, אותו מודל של NOAA)
        [Theory]
        [InlineData("2026-10-23", "17:58")]   // שישי, שעון קיץ
        [InlineData("2026-10-31", "16:50")]   // שבת, אחרי המעבר לשעון חורף
        [InlineData("2026-06-20", "19:47")]   // שבת, אמצע הקיץ
        [InlineData("2026-12-05", "16:35")]   // שבת, מהשקיעות המוקדמות בשנה
        [InlineData("2027-03-26", "18:54")]   // שישי, היום שבו עוברים לשעון קיץ
        public void Sunset_InJerusalem_MatchesTheReference(string date, string expectedLocal)
        {
            var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sunset = ShabbatClock.SunsetUtc(day, Policy.ShabbatLatitude, Policy.ShabbatLongitude);

            AssertClose(day + TimeSpan.ParseExact(expectedLocal, @"hh\:mm", CultureInfo.InvariantCulture), sunset);
        }

        [Fact]
        public void Window_StartsAtCandleLighting_AndEndsAfterShabbat()
        {
            // שבת 23-24/10/2026: שקיעה בשישי 17:58 -> כניסה 17:18; שקיעה בשבת 17:57 -> צאת השבת 18:37
            var window = ShabbatClock.ForSaturday(new DateTime(2026, 10, 24), Policy);

            AssertClose(new DateTime(2026, 10, 23, 17, 18, 0), window.StartUtc);
            AssertClose(new DateTime(2026, 10, 24, 18, 37, 0), window.EndUtc);
        }

        [Theory]
        [InlineData(2026, 10, 19, 10, 0, false)]   // יום שני
        [InlineData(2026, 10, 23, 16, 0, false)]   // שישי לפני כניסת השבת
        [InlineData(2026, 10, 23, 17, 30, true)]   // שישי אחרי כניסת השבת
        [InlineData(2026, 10, 24, 12, 0, true)]    // שבת בצהריים
        [InlineData(2026, 10, 24, 18, 20, true)]   // אחרי השקיעה, לפני צאת השבת
        [InlineData(2026, 10, 24, 19, 0, false)]   // מוצאי שבת
        [InlineData(2026, 10, 25, 10, 0, false)]   // יום ראשון
        public void IsShabbat_FollowsTheWindow(int y, int mo, int d, int h, int mi, bool expected) =>
            Assert.Equal(expected, ShabbatClock.IsShabbat(Utc(y, mo, d, h, mi), Policy));

        [Fact]
        public void Overlaps_ARouteThatRunsIntoShabbat()
        {
            Assert.True(ShabbatClock.Overlaps(Utc(2026, 10, 23, 17, 0), Utc(2026, 10, 23, 17, 40), Policy));
            Assert.True(ShabbatClock.Overlaps(Utc(2026, 10, 23, 10, 0), Utc(2026, 10, 25, 10, 0), Policy));
            Assert.False(ShabbatClock.Overlaps(Utc(2026, 10, 23, 15, 0), Utc(2026, 10, 23, 16, 30), Policy));
            Assert.False(ShabbatClock.Overlaps(Utc(2026, 10, 24, 19, 0), Utc(2026, 10, 24, 22, 0), Policy));
        }

        [Fact]
        public void ObserveShabbatOff_NothingIsBlocked()
        {
            var off = new MatchingPolicyOptions { ObserveShabbat = false };
            Assert.False(ShabbatClock.IsShabbat(Utc(2026, 10, 24, 12, 0), off));
            Assert.False(ShabbatClock.Overlaps(Utc(2026, 10, 23, 17, 0), Utc(2026, 10, 24, 12, 0), off));
        }

        [Fact]
        public void PolicyOverride_CanTurnTheShabbatGuardOff()
        {
            var policy = MatchingPolicyProvider.Apply(new MatchingPolicyOptions(),
                new System.Collections.Generic.Dictionary<string, string> { [nameof(MatchingPolicyOptions.ObserveShabbat)] = "false" });
            Assert.False(policy.ObserveShabbat);
        }

        // ---------------- עמותה: סגורה בשבת, פתוחה במוצאי שבת רק אם הגדירה שעות ----------------

        [Fact]
        public void Ngo_OnSaturday_OnlyTheMotzeiShabbatHours()
        {
            var ngo = new Ngo { SunToThuOpenTime = TimeSpan.Zero, SunToThuCloseTime = new TimeSpan(23, 59, 0) };
            Assert.False(NgoAvailabilityHelper.IsNgoOpenAt(ngo, Utc(2026, 10, 24, 20, 0)));

            ngo.ReceivesOnSaturdayNight = true;
            ngo.SaturdayNightOpenTime = new TimeSpan(19, 0, 0);
            ngo.SaturdayNightCloseTime = new TimeSpan(23, 0, 0);

            Assert.True(NgoAvailabilityHelper.IsNgoOpenAt(ngo, Utc(2026, 10, 24, 20, 0)));
            Assert.False(NgoAvailabilityHelper.IsNgoOpenAt(ngo, Utc(2026, 10, 24, 12, 0)));
            Assert.False(NgoAvailabilityHelper.IsNgoOpenAt(ngo, Utc(2026, 10, 24, 23, 30)));
            Assert.True(NgoAvailabilityHelper.IsNgoOpenAt(ngo, Utc(2026, 10, 25, 10, 0)));   // ראשון - השעות הרגילות
        }

        private static async Task<(NgoService Service, EngineHarness H)> NgoServiceAsync()
        {
            var h = new EngineHarness();
            await h.SeedBasicsAsync();

            var geocoding = new Mock<IGeocodingService>();
            geocoding.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodeResult(32.09, 34.79, "רחוב העמותה 5", "place-id"));

            var service = new NgoService(h.Ngos, h.Users, new RegionRepository(h.Db), new Mock<IApprovalRequestService>().Object,
                h.Caller, h.UnitOfWork, h.NotificationService, TestDb.Mapper, TestDb.Log<NgoService>(),
                new PasswordHasherWithLegacySupport(), geocoding.Object,
                new RegistrationNoticeService(h.NotificationService, h.Notifications, TestDb.Config()));

            return (service, h);
        }

        private static UpdateNgoHoursDto Hours(bool saturdayNight, string? open, string? close) => new()
        {
            Address = "רחוב העמותה 5",
            SunToThuOpenTime = new TimeSpan(9, 0, 0),
            SunToThuCloseTime = new TimeSpan(17, 0, 0),
            ReceivesOnSaturdayNight = saturdayNight,
            SaturdayNightOpenTime = open == null ? null : TimeSpan.Parse(open, CultureInfo.InvariantCulture),
            SaturdayNightCloseTime = close == null ? null : TimeSpan.Parse(close, CultureInfo.InvariantCulture)
        };

        [Fact]
        public async Task NgoHours_SaturdayNight_IsValidated_AndSaved()
        {
            var (service, h) = await NgoServiceAsync();
            using var _ = h;
            int ngoUserId = h.Ngo.UserId;

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateMyHoursAsync(ngoUserId, Hours(true, "12:00", "15:00")));
            Assert.Contains("מוצאי שבת", ex.Message);
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateMyHoursAsync(ngoUserId, Hours(true, "21:00", "20:00")));
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateMyHoursAsync(ngoUserId, Hours(true, null, null)));

            await service.UpdateMyHoursAsync(ngoUserId, Hours(true, "19:30", "22:00"));
            h.Fresh();
            var ngo = await h.Db.Ngos.SingleAsync(n => n.UserId == ngoUserId);
            Assert.True(ngo.ReceivesOnSaturdayNight);
            Assert.Equal(new TimeSpan(19, 30, 0), ngo.SaturdayNightOpenTime);
            Assert.Equal(new TimeSpan(22, 0, 0), ngo.SaturdayNightCloseTime);

            // כיבוי - השעות נמחקות
            await service.UpdateMyHoursAsync(ngoUserId, Hours(false, "19:30", "22:00"));
            h.Fresh();
            ngo = await h.Db.Ngos.SingleAsync(n => n.UserId == ngoUserId);
            Assert.False(ngo.ReceivesOnSaturdayNight);
            Assert.Null(ngo.SaturdayNightOpenTime);
        }

        // ---------------- מנוע השידוכים ----------------

        [Fact]
        public async Task Engine_OnShabbat_SendsNoOffers_AndDoesNotEscalate()
        {
            using var h = new EngineHarness(localNow: new DateTime(2026, 10, 24, 12, 0, 0));   // שבת בצהריים
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");
            var d = await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync("Manual");
            Assert.Equal(0, result.RoutesCreated);
            Assert.Contains(ShabbatClock.EngineMessage, result.FailedConstraints);

            // גם שיבוץ מחדש (אחרי דחייה / אי-מענה) לא שולח הצעה, ולא נכנס ללולאת ניסיונות חוזרים של Hangfire
            var outcome = await h.Engine.MatchDonationAsync(d.Id, "Reassign");
            Assert.NotNull(outcome);
            Assert.False(outcome!.Matched);
            Assert.Contains("שבת", outcome.Message);
            await h.Engine.ReassignDonationAsync(d.Id);

            h.Fresh();
            Assert.Empty(await h.Db.Matches.ToListAsync());
            Assert.Empty(await h.Db.Notifications.Where(n => n.UserId == h.AdminUser.Id).ToListAsync());
        }

        [Fact]
        public async Task Engine_OnFriday_NoRouteThatRunsIntoShabbat()
        {
            // שישי 17:15 - כניסת השבת ב-17:18, והמסלול (כולל זמן המענה של המתנדב) מסתיים אחריה
            using var late = new EngineHarness(localNow: new DateTime(2026, 10, 23, 17, 15, 0));
            await late.SeedBasicsAsync();
            await late.AddVolunteerAsync("vol@test");
            await late.AddDonationAsync();

            var result = await late.Engine.RunBatchAsync("Manual");
            Assert.Equal(0, result.RoutesCreated);
            Assert.Contains(result.FailedConstraints, f => f.Contains("כניסת השבת"));

            // אותו מצב בשעה 16:00 - יש מספיק זמן לפני השבת
            using var early = new EngineHarness(localNow: new DateTime(2026, 10, 23, 16, 0, 0));
            await early.SeedBasicsAsync();
            await early.AddVolunteerAsync("vol@test");
            await early.AddDonationAsync();

            Assert.Equal(1, (await early.Engine.RunBatchAsync("Manual")).RoutesCreated);
        }

        [Fact]
        public async Task Engine_OnMotzeiShabbat_MatchesWhenTheNgoReceivesThen()
        {
            // מוצאי שבת 19:30 (השבת יצאה ב-18:37)
            using var h = new EngineHarness(localNow: new DateTime(2026, 10, 24, 19, 30, 0));
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test", withSlot: false);
            h.Db.AvailabilitySlots.Add(new AvailabilitySlot
            {
                VolunteerId = v.Id, RegionId = h.Region.Id, Day = DayOfWeek.Saturday,
                StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(23, 30, 0)
            });
            await h.Db.SaveChangesAsync();
            await h.AddDonationAsync(pickupIn: TimeSpan.FromHours(2));

            // העמותה עוד לא הגדירה שעות למוצאי שבת - סגורה כל השבת
            Assert.Equal(0, (await h.Engine.RunBatchAsync("Manual")).RoutesCreated);

            h.Ngo.ReceivesOnSaturdayNight = true;
            h.Ngo.SaturdayNightOpenTime = new TimeSpan(19, 0, 0);
            h.Ngo.SaturdayNightCloseTime = new TimeSpan(23, 0, 0);
            await h.Db.SaveChangesAsync();

            Assert.Equal(1, (await h.Engine.RunBatchAsync("Manual")).RoutesCreated);
            h.Fresh();
            Assert.Equal(v.Id, (await h.Db.Matches.SingleAsync()).VolunteerId);
        }
    }
}
