using BLL.Algorithms;
using BLL.Services;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs.Validation;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace FoodRescue.Tests
{
    // תיקון 39: מתנדב עם רישיון נהיגה שלא בתוקף - המנהל לא יכול לאשר אותו, ו"היום" נקבע לפי שעון ישראל בכל הבדיקות
    public sealed class DriverLicenseApprovalTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        [Fact]
        public void Today_IsTheIsraeliDate_SameAsTheApprovalCheck()
        {
            // 22:30 UTC ב-9.11 = 00:30 ב-10.11 בישראל. בעבר ההרשמה בדקה לפי UTC (9.11) והאישור לפי ישראל (10.11)
            var utc = new DateTime(2026, 11, 9, 22, 30, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 11, 10), IsraelDate.TodayAt(utc));
            Assert.Equal(DriverLicensePolicy.TodayLocal(utc), IsraelDate.TodayAt(utc));
            Assert.False(DriverLicensePolicy.IsValid(new DateTime(2026, 11, 9), utc));   // תוקף עד אתמול
            Assert.True(DriverLicensePolicy.IsValid(new DateTime(2026, 11, 10), utc));   // תוקף עד היום - עדיין בתוקף
        }

        [Fact]
        public void TheRegistrationForm_RejectsALicenseThatExpiredYesterday_AndAcceptsToday()
        {
            var attribute = new NotInPastAttribute();

            Assert.False(attribute.IsValid(IsraelDate.Today.AddDays(-1)));
            Assert.True(attribute.IsValid(IsraelDate.Today));
            Assert.True(attribute.IsValid(null));
        }

        private ApprovalRequestService Approvals() => new(
            new ApprovalRequestRepository(_h.Db), new VolunteerRepository(_h.Db), new SupplierRepository(_h.Db), new NgoRepository(_h.Db),
            new ManagerRepository(_h.Db), new UserRepository(_h.Db), new UnitOfWork(_h.Db), new ValidationService(),
            _h.NotificationService, _h.ActionTokens, TestDb.Config(), TestDb.Mapper, TestDb.Log<ApprovalRequestService>());

        [Fact]
        public async Task ApprovingAVolunteerWithAnExpiredLicense_IsRefused_AndNothingChanges()
        {
            await _h.SeedBasicsAsync();
            // הבקשה חיכתה לאישור, ובינתיים הרישיון פג
            var volunteer = await _h.AddVolunteerAsync("expired@test", ApprovalStatus.Pending,
                licenseExpiry: DateTime.UtcNow.AddDays(-3));
            _h.Fresh();

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                Approvals().ApproveProfileAsync(FoodRescue.DTOs.RequestedRole.Volunteer, volunteer.Id, _h.AdminUser.Id));
            Assert.Contains("רישיון הנהיגה אינו בתוקף", error.Message);

            _h.Fresh();
            Assert.Equal(ApprovalStatus.Pending, (await _h.Db.Volunteers.SingleAsync(v => v.Id == volunteer.Id)).ApprovalStatus);
            Assert.DoesNotContain(await _h.Db.Notifications.ToListAsync(), n => n.UserId == volunteer.UserId);
        }

        [Fact]
        public async Task ApprovingAVolunteerWithAValidLicense_Works()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("valid@test", ApprovalStatus.Pending, licenseExpiry: DateTime.UtcNow.AddYears(1));
            _h.Fresh();

            await Approvals().ApproveProfileAsync(FoodRescue.DTOs.RequestedRole.Volunteer, volunteer.Id, _h.AdminUser.Id);

            _h.Fresh();
            Assert.Equal(ApprovalStatus.Approved, (await _h.Db.Volunteers.SingleAsync(v => v.Id == volunteer.Id)).ApprovalStatus);
        }

        [Fact]
        public void TheAutomaticCheck_UsesTheIsraeliDate()
        {
            var today = DriverLicensePolicy.TodayLocal(DateTime.UtcNow);

            var (expiredPassed, expiredDetails) = new ValidationService()
                .ValidateVolunteer("000000018", "1234567", today.AddDays(-1), new DateTime(1990, 1, 1));
            var (validPassed, _) = new ValidationService()
                .ValidateVolunteer("000000018", "1234567", today.AddDays(60), new DateTime(1990, 1, 1));

            Assert.False(expiredPassed);
            Assert.Contains("פג", expiredDetails);
            Assert.True(validPassed);
        }
    }
}
