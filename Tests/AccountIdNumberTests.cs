using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace FoodRescue.Tests
{
    // תיקון 32: מי שמוסיף לעצמו תפקיד לא מזין שוב תעודת זהות שכבר שמורה בחשבון
    public sealed class AccountIdNumberTests : IDisposable
    {
        private const string PersonalId = "123456782";   // ת"ז תקינה (ספרת ביקורת)
        private const string CompanyId = "515555555";    // ח.פ של חברה - ספרת ביקורת תקינה, אבל מתחיל ב-5

        private readonly EngineHarness _h = new();
        private FoodRescueDbContext Db => _h.Db;

        public void Dispose() => _h.Dispose();

        // ---------------- העזר ----------------

        [Theory]
        [InlineData(PersonalId, true)]
        [InlineData("034567891", true)]
        [InlineData("12345678-2", true)]     // עם מקף
        [InlineData(CompanyId, false)]       // חברה
        [InlineData("580000008", false)]     // עמותה
        [InlineData("123456789", false)]     // ספרת ביקורת שגויה
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPersonalId(string? number, bool expected) =>
            Assert.Equal(expected, AccountIdNumber.IsPersonalId(number));

        [Fact]
        public void Mask_ShowsOnlyTheLastFourDigits()
        {
            Assert.Equal("•••••6782", AccountIdNumber.Mask(PersonalId));
            Assert.Equal("•••", AccountIdNumber.Mask("123"));
        }

        [Fact]
        public void Find_PrefersTheVolunteerId_ThenAPersonalTaxId()
        {
            Assert.Equal("000000018", AccountIdNumber.Find(new Volunteer { IdNumber = "00018" }, new Supplier { TaxId = PersonalId }));   // מנורמל ל-9 ספרות
            Assert.Equal(PersonalId, AccountIdNumber.Find(null, new Supplier { TaxId = PersonalId }));
            Assert.Null(AccountIdNumber.Find(null, new Supplier { TaxId = CompanyId }));
            Assert.Null(AccountIdNumber.Find(null, null));
        }

        // ---------------- ספק שמוסיף תפקיד מתנדב ----------------

        private RegistrationNoticeService Notices() => new(_h.NotificationService, _h.Notifications, TestDb.Config());

        private ApprovalRequestService Approvals() => new(
            new ApprovalRequestRepository(Db), new VolunteerRepository(Db), new SupplierRepository(Db), new NgoRepository(Db),
            new ManagerRepository(Db), new UserRepository(Db), new UnitOfWork(Db), new ValidationService(),
            _h.NotificationService, _h.ActionTokens, TestDb.Config(), TestDb.Mapper, TestDb.Log<ApprovalRequestService>());

        private VolunteerService Volunteers() => new(
            _h.Volunteers, _h.Users, new AvailabilitySlotRepository(Db), new RegionRepository(Db), _h.Routes,
            Approvals(), new ValidationService(), _h.UnitOfWork, _h.NotificationService,
            TestDb.Mapper, TestDb.Log<VolunteerService>(), new PasswordHasherWithLegacySupport(), Notices(), _h.Tracker,
            _h.Suppliers, clock: _h.Clock);

        private static CreateVolunteerDto VolunteerForm(string? idNumber) => new()
        {
            VehicleType = "רכב פרטי", MaxCapacityKg = 80, IsAvailable = true, IdNumber = idNumber,
            DriverLicenseNumber = "1234567", DriverLicenseExpiry = DateTime.UtcNow.AddYears(2), DateOfBirth = new DateTime(1990, 1, 1)
        };

        private async Task<User> SupplierWithTaxIdAsync(string taxId)
        {
            await _h.SeedBasicsAsync();
            var supplier = await Db.Suppliers.SingleAsync();
            supplier.TaxId = taxId;
            await Db.SaveChangesAsync();
            _h.Fresh();
            return await Db.Users.SingleAsync(u => u.Email == "supplier@test");
        }

        [Fact]
        public async Task SoleProprietor_AddsTheVolunteerRole_WithoutTypingTheIdAgain()
        {
            var user = await SupplierWithTaxIdAsync(PersonalId);

            // הפרופיל מראה רק גרסה מוסתרת - המספר המלא לא יוצא מהשרת
            var profile = await _h.CreateUserService().GetMyProfileAsync(user.Id);
            Assert.Equal("•••••6782", profile!.KnownIdNumberMasked);

            Assert.Equal(AddRoleResult.Added, await Volunteers().AddRoleAsync(user.Id, VolunteerForm(idNumber: null)));

            _h.Fresh();
            var volunteer = await Db.Volunteers.SingleAsync(v => v.UserId == user.Id);
            Assert.Equal(PersonalId, volunteer.IdNumber);
            Assert.Equal(ApprovalStatus.Pending, volunteer.ApprovalStatus);
            var request = await Db.ApprovalRequests.SingleAsync(r => r.UserId == user.Id);
            Assert.True(request.PassedAutomaticValidation);   // הת"ז מהחשבון עברה את הסינון האוטומטי
        }

        [Fact]
        public async Task CompanySupplier_StillHasToEnterAnId()
        {
            var user = await SupplierWithTaxIdAsync(CompanyId);

            Assert.Null((await _h.CreateUserService().GetMyProfileAsync(user.Id))!.KnownIdNumberMasked);

            var error = await Assert.ThrowsAsync<ArgumentException>(() => Volunteers().AddRoleAsync(user.Id, VolunteerForm(idNumber: "")));
            Assert.Contains("תעודת זהות", error.Message);
            Assert.False(await Db.Volunteers.AnyAsync(v => v.UserId == user.Id));
        }

        [Fact]
        public async Task AnIdTypedInTheForm_WinsOverTheAccountId()
        {
            var user = await SupplierWithTaxIdAsync(PersonalId);

            Assert.Equal(AddRoleResult.Added, await Volunteers().AddRoleAsync(user.Id, VolunteerForm("034567891")));

            _h.Fresh();
            Assert.Equal("034567891", (await Db.Volunteers.SingleAsync(v => v.UserId == user.Id)).IdNumber);
        }

        // ---------------- הכיוון ההפוך: מתנדב (עוסק מורשה) שמוסיף תפקיד ספק ----------------

        private SupplierService Suppliers()
        {
            var licenses = new Mock<ILicenseFileService>();
            licenses.Setup(l => l.SaveLicenseAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>())).ReturnsAsync("license.pdf");
            var geocoding = new Mock<IGeocodingService>();
            geocoding.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodeResult(32.08, 34.78, "רחוב העסק 1, תל אביב", "place-id"));

            return new SupplierService(_h.Suppliers, _h.Users, Approvals(), licenses.Object, geocoding.Object, _h.UnitOfWork,
                _h.NotificationService, TestDb.Mapper, TestDb.Log<SupplierService>(), new PasswordHasherWithLegacySupport(),
                Notices(), _h.Volunteers);
        }

        private static CreateSupplierDto SupplierForm(string? taxId, bool useAccountId) => new()
        {
            BusinessName = "הקונדיטוריה של דנה", Address = "רחוב העסק 1, תל אביב", TaxId = taxId, UseAccountIdNumber = useAccountId,
            LicenseFile = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "LicenseFile", "license.pdf")
        };

        [Fact]
        public async Task Volunteer_AddsTheSupplierRole_WithTheIdAsTheBusinessNumber()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("dana@test");          // ת"ז 000000018

            Assert.Equal(AddRoleResult.Added, await Suppliers().AddRoleAsync(volunteer.UserId, SupplierForm(taxId: null, useAccountId: true)));

            _h.Fresh();
            var supplier = await Db.Suppliers.SingleAsync(s => s.UserId == volunteer.UserId);
            Assert.Equal("000000018", supplier.TaxId);
            Assert.Equal(ApprovalStatus.Pending, supplier.ApprovalStatus);
        }

        [Fact]
        public async Task WithoutTheCheckbox_TheBusinessNumberIsRequired()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("dana@test");

            await Assert.ThrowsAsync<ArgumentException>(() => Suppliers().AddRoleAsync(volunteer.UserId, SupplierForm(taxId: null, useAccountId: false)));
            Assert.False(await Db.Suppliers.AnyAsync(s => s.UserId == volunteer.UserId));
        }
    }
}
