using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Security;
using BLL.Services;
using DAL.Classes;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 77: מחיקת חשבון בידי המשתמש - מחיקה רכה (IsActive = false) על מסד בזיכרון
    public sealed class AccountDeletionTests : IDisposable
    {
        private const string Password = "Password123!";   // הסיסמה של כל המשתמשים ב-EngineHarness
        private readonly EngineHarness _h = new();

        private AccountDeletionService Service() => new(
            _h.Users, _h.Volunteers, _h.Suppliers, _h.Ngos, _h.Routes, _h.Donations, _h.Matches, _h.UnitOfWork,
            new PasswordHasherWithLegacySupport(), _h.NotificationService, TestDb.Config(),
            TestDb.Log<AccountDeletionService>(), _h.Clock);

        private static DeleteAccountDto WithPassword(string password = Password) => new() { Password = password };

        public void Dispose() => _h.Dispose();

        [Fact]
        public async Task Volunteer_DeletesAccount_IsDeactivated_TokensRevoked_AndLeavesTheEngine()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var oldStamp = (await _h.Users.GetByIdAsync(volunteer.UserId))!.SecurityStamp;

            await Service().DeleteMyAccountAsync(volunteer.UserId, WithPassword());

            _h.Fresh();
            var user = (await _h.Users.GetByIdAsync(volunteer.UserId))!;
            Assert.False(user.IsActive);
            Assert.NotNull(user.DeactivatedAt);
            Assert.NotEqual(oldStamp, user.SecurityStamp);

            // הטוקן הישן נפסל (גם עם החותמת החדשה - כי החשבון לא פעיל)
            var sessions = new SessionValidationService(_h.Users);
            Assert.Null(await sessions.ValidateAsync(user.Id, oldStamp));
            Assert.Null(await sessions.ValidateAsync(user.Id, user.SecurityStamp));

            // המנוע לא רואה אותו יותר
            Assert.False((await _h.Volunteers.GetByUserIdAsync(user.Id))!.IsAvailable);
            var candidates = await _h.Volunteers.GetAvailableVolunteersAsync(_h.LocalNow.Date);
            Assert.DoesNotContain(candidates, v => v.UserId == user.Id);

            // השורה נשארת במסד - ההיסטוריה של האחרים שלמה
            Assert.NotNull(await _h.Users.GetByIdAsync(user.Id));
        }

        [Fact]
        public async Task DeletedAccount_CannotLogIn_AndGetsAClearMessage()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            await Service().DeleteMyAccountAsync(volunteer.UserId, WithPassword());
            _h.Fresh();

            var result = await _h.CreateUserService().LoginAsync(new LoginDto { Email = "vol@test", Password = Password });

            Assert.False(result.Succeeded);
            Assert.Equal(AuthError.AccountDeleted, result.Error);
            Assert.Contains("נמחק", result.Message);

            // עם סיסמה שגויה - אותה תשובה כמו לכל מייל (לא חושפים שהחשבון קיים או נמחק)
            var wrong = await _h.CreateUserService().LoginAsync(new LoginDto { Email = "vol@test", Password = "wrong-pass-1" });
            Assert.Equal(AuthError.InvalidCredentials, wrong.Error);
        }

        // תיקון 78: מנהל משחזר את החשבון - המתנדב מתחבר שוב עם אותה סיסמה וחוזר למנוע
        [Fact]
        public async Task Admin_RestoresADeletedAccount_TheVolunteerCanLogInAndBeMatchedAgain()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            await Service().DeleteMyAccountAsync(volunteer.UserId, WithPassword());
            _h.Fresh();

            await Service().RestoreAccountAsync(volunteer.UserId);
            _h.Fresh();

            var user = (await _h.Users.GetByIdAsync(volunteer.UserId))!;
            Assert.True(user.IsActive);
            Assert.Null(user.DeactivatedAt);

            var login = await _h.CreateUserService().LoginAsync(new LoginDto { Email = "vol@test", Password = Password });
            Assert.True(login.Succeeded);

            Assert.True((await _h.Volunteers.GetByUserIdAsync(user.Id))!.IsAvailable);
            var candidates = await _h.Volunteers.GetAvailableVolunteersAsync(_h.LocalNow.Date);
            Assert.Contains(candidates, v => v.UserId == user.Id);

            // תיקון 79: נשלח מייל אחד "החשבון שלך שוחזר" לכתובת של המשתמש
            var emails = _h.Db.Notifications
                .Where(n => n.UserId == user.Id && n.Channel == NotificationChannel.Email
                            && n.Subject == AccountDeletionService.RestoredSubject)
                .ToList();
            var email = Assert.Single(emails);
            Assert.Equal("vol@test", email.Recipient);
        }

        [Fact]
        public async Task Restore_OfAnActiveOrMissingAccount_IsRejected()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");

            await Assert.ThrowsAsync<ConflictException>(() => Service().RestoreAccountAsync(volunteer.UserId));
            await Assert.ThrowsAsync<NotFoundException>(() => Service().RestoreAccountAsync(999_999));
        }

        [Fact]
        public async Task WrongPassword_DoesNotDeleteTheAccount()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                Service().DeleteMyAccountAsync(volunteer.UserId, WithPassword("not-my-password")));

            _h.Fresh();
            Assert.True((await _h.Users.GetByIdAsync(volunteer.UserId))!.IsActive);
        }

        [Fact]
        public async Task Admin_CannotDeleteTheAccountFromTheProfile()
        {
            await _h.SeedBasicsAsync();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service().DeleteMyAccountAsync(_h.AdminUser.Id, WithPassword()));
        }

        [Fact]
        public async Task Supplier_WithADonationInProgress_MustFinishFirst()
        {
            await _h.SeedBasicsAsync();
            await _h.AddDonationAsync();   // ממתינה - עוד בטיפול

            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Service().DeleteMyAccountAsync(_h.Supplier.UserId, WithPassword()));

            Assert.Contains("תרומות", error.Message);
            _h.Fresh();
            Assert.True((await _h.Users.GetByIdAsync(_h.Supplier.UserId))!.IsActive);
        }

        [Fact]
        public async Task Supplier_WithOnlyFinishedDonations_CanDelete()
        {
            await _h.SeedBasicsAsync();
            var donation = await _h.AddDonationAsync();
            donation.Status = DonationStatus.Completed;
            await _h.Db.SaveChangesAsync();

            await Service().DeleteMyAccountAsync(_h.Supplier.UserId, WithPassword());

            _h.Fresh();
            Assert.False((await _h.Users.GetByIdAsync(_h.Supplier.UserId))!.IsActive);
        }

        [Fact]
        public async Task Volunteer_WithAnActiveRoute_MustFinishFirst()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var donation = await _h.AddDonationAsync();
            donation.Status = DonationStatus.Matched;

            var route = new VolunteerRoute { VolunteerId = volunteer.Id, Status = RouteStatus.Planned, ScheduledAt = _h.NowUtc };
            _h.Db.VolunteerRoutes.Add(route);
            await _h.Db.SaveChangesAsync();
            _h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id, VolunteerRouteId = route.Id,
                IsActive = true, Status = MatchStatus.Confirmed, AssignedAt = _h.NowUtc
            });
            await _h.Db.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Service().DeleteMyAccountAsync(volunteer.UserId, WithPassword()));

            Assert.Contains("מסלול", error.Message);
        }

        [Fact]
        public async Task Ngo_WithADonationOnTheWay_MustConfirmFirst_AndAfterDeletionGetsNoDonations()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var donation = await _h.AddDonationAsync();
            donation.Status = DonationStatus.Delivered;      // נמסרה - ממתינה לאישור קבלה של העמותה
            _h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id,
                IsActive = true, Status = MatchStatus.Completed, AssignedAt = _h.NowUtc
            });
            await _h.Db.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() =>
                Service().DeleteMyAccountAsync(_h.Ngo.UserId, WithPassword()));

            // העמותה אישרה קבלה - עכשיו אפשר למחוק
            donation.Status = DonationStatus.Completed;
            await _h.Db.SaveChangesAsync();
            await Service().DeleteMyAccountAsync(_h.Ngo.UserId, WithPassword());

            _h.Fresh();
            Assert.DoesNotContain(await _h.Ngos.GetApprovedAsync(), n => n.Id == _h.Ngo.Id);
        }
    }
}
