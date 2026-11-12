using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.Controllers;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace FoodRescue.Tests
{
    // תיקון 10: חשבון אחד (מייל אחד) עם כמה תפקידים - למשל ספק שהוא גם מתנדב
    public sealed class MultiRoleTests : IDisposable
    {
        private const string Password = "Password123!";   // הסיסמה של כל המשתמשים ב-EngineHarness

        private readonly EngineHarness _h = new();
        private FoodRescueDbContext Db => _h.Db;

        public void Dispose() => _h.Dispose();

        private sealed class NoSms : ISmsSender
        {
            public SmsMode Mode => SmsMode.NotConfigured;
            public Task<string?> SendAsync(string to, string text) => Task.FromResult<string?>(null);
        }

        private RegistrationNoticeService Notices() => new(_h.NotificationService, _h.Notifications, TestDb.Config());

        private UserService Users() => new(
            new UserRepository(Db), new NgoRepository(Db), new SupplierRepository(Db), new VolunteerRepository(Db),
            new LoginLockoutService(new LoginAttemptRepository(Db)),
            _h.ActionTokens, _h.NotificationService,
            new TokenService(TestDb.Config()), TestDb.Config(),
            TestDb.Log<UserService>(), TestDb.Mapper, new PasswordHasherWithLegacySupport(), Notices(), new NoSms());

        private ApprovalRequestService Approvals() => new(
            new ApprovalRequestRepository(Db), new VolunteerRepository(Db), new SupplierRepository(Db), new NgoRepository(Db),
            new ManagerRepository(Db), new UserRepository(Db), new UnitOfWork(Db), new ValidationService(),
            _h.NotificationService, _h.ActionTokens, TestDb.Config(), TestDb.Mapper, TestDb.Log<ApprovalRequestService>());

        private VolunteerService Volunteers() => new(
            _h.Volunteers, _h.Users, new AvailabilitySlotRepository(Db), new RegionRepository(Db), _h.Routes,
            Approvals(), new ValidationService(), _h.UnitOfWork, _h.NotificationService,
            TestDb.Mapper, TestDb.Log<VolunteerService>(), new PasswordHasherWithLegacySupport(), Notices(), _h.Tracker, clock: _h.Clock);

        private NgoService Ngos()
        {
            var geocoding = new Mock<IGeocodingService>();
            geocoding.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodeResult(32.09, 34.79, "רחוב העמותה 5", "place-id"));

            return new NgoService(_h.Ngos, _h.Users, new RegionRepository(Db), Approvals(), _h.Caller, _h.UnitOfWork,
                _h.NotificationService, TestDb.Mapper, TestDb.Log<NgoService>(), new PasswordHasherWithLegacySupport(),
                geocoding.Object, Notices());
        }

        private static CreateVolunteerDto VolunteerForm() => new()
        {
            VehicleType = "רכב פרטי", MaxCapacityKg = 80, IsAvailable = true, IdNumber = "000000018",
            DriverLicenseNumber = "1234567", DriverLicenseExpiry = DateTime.UtcNow.AddYears(2), DateOfBirth = new DateTime(1990, 1, 1)
        };

        private Task<AuthResult> LoginAsync(string email) =>
            Users().LoginAsync(new LoginDto { Email = email, Password = Password });

        // ---------------- התרחיש המלא: ספק שמצטרף גם כמתנדב ----------------

        [Fact]
        public async Task Supplier_AddsVolunteerRole_KeepsWorking_ThenHasBothRoles()
        {
            await _h.SeedBasicsAsync();
            var supplierUser = await Db.Users.SingleAsync(u => u.Email == "supplier@test");

            Assert.Equal(AddRoleResult.Added, await Volunteers().AddRoleAsync(supplierUser.Id, VolunteerForm()));

            _h.Fresh();
            var volunteer = await Db.Volunteers.SingleAsync(v => v.UserId == supplierUser.Id);
            Assert.Equal(ApprovalStatus.Pending, volunteer.ApprovalStatus);
            Assert.Contains(await Db.ApprovalRequests.ToListAsync(),
                r => r.UserId == supplierUser.Id && r.RequestedRole == RequestedRole.Volunteer && r.Status == ApprovalStatus.Pending);

            // עד האישור: ממשיכים להיכנס כספק (בעבר: מתנדב ממתין חסם את כל החשבון)
            var beforeApproval = await LoginAsync("supplier@test");
            Assert.True(beforeApproval.Succeeded, beforeApproval.Message);
            Assert.Equal("Supplier", beforeApproval.Response!.Role);
            Assert.Equal(new[] { "Supplier" }, beforeApproval.Response.Roles);

            var profile = await Users().GetMyProfileAsync(supplierUser.Id);
            Assert.True(profile!.CanAddRoles);
            Assert.Contains(profile.Roles, r => r.Role == "Supplier" && r.Status == "Approved");
            Assert.Contains(profile.Roles, r => r.Role == "Volunteer" && r.Status == "Pending");

            // אותו תפקיד פעמיים - לא נוצר פרופיל נוסף
            Assert.Equal(AddRoleResult.AlreadyHasRole, await Volunteers().AddRoleAsync(supplierUser.Id, VolunteerForm()));
            Assert.Single(await Db.Volunteers.Where(v => v.UserId == supplierUser.Id).ToListAsync());

            // המנהל מאשר את התפקיד הנוסף
            await Approvals().ApproveProfileAsync(FoodRescue.DTOs.RequestedRole.Volunteer, volunteer.Id, _h.AdminUser.Id);

            var afterApproval = await LoginAsync("supplier@test");
            Assert.True(afterApproval.Succeeded);
            Assert.Equal("Supplier", afterApproval.Response!.Role);   // התפקיד הראשי לא השתנה
            Assert.Contains("Supplier", afterApproval.Response.Roles);
            Assert.Contains("Volunteer", afterApproval.Response.Roles);

            _h.Fresh();
            Assert.Equal(UserRole.Supplier, (await Db.Users.SingleAsync(u => u.Id == supplierUser.Id)).Role);
            Assert.Contains(await Db.Notifications.Where(n => n.UserId == supplierUser.Id).ToListAsync(),
                n => n.Channel == NotificationChannel.InApp && n.Subject == "בקשת ההצטרפות שלך כמתנדב/ת אושרה");

            // הקישור במייל האישור מכניס ישר לאזור המתנדב
            var link = await _h.ActionTokens.CreateLinkTokenAsync(supplierUser.Id, ActionTokenPurpose.LoginLink, TimeSpan.FromDays(3),
                (int)RequestedRole.Volunteer);
            var viaLink = await Users().LinkLoginAsync(new LinkLoginDto { Token = link });
            Assert.Equal(LinkLoginStatus.Success, viaLink.Status);
            Assert.Equal("Volunteer", viaLink.Response!.Role);
            Assert.Equal("/app/volunteer/availability", viaLink.Response.NextPath);
            Assert.Contains("Supplier", viaLink.Response.Roles);
        }

        [Fact]
        public async Task PrimaryRoleStillPending_LogsInWithTheApprovedRole()
        {
            await _h.SeedBasicsAsync();
            var supplierUser = await Db.Users.SingleAsync(u => u.Email == "supplier@test");
            supplierUser.Role = UserRole.Volunteer;   // נרשם קודם כמתנדב (עוד לא אושר), ובינתיים אושר כספק
            Db.Volunteers.Add(new Volunteer
            {
                UserId = supplierUser.Id, VehicleType = "רכב", MaxCapacityKg = 50, IdNumber = "000000018",
                DriverLicenseNumber = "1234567", DriverLicenseExpiry = DateTime.UtcNow.AddYears(1),
                DateOfBirth = new DateTime(1990, 1, 1), ApprovalStatus = ApprovalStatus.Pending, RowVersion = new byte[8]
            });
            await Db.SaveChangesAsync();

            var result = await LoginAsync("supplier@test");

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal("Supplier", result.Response!.Role);
        }

        [Fact]
        public async Task NoApprovedRole_IsStillBlocked()
        {
            await _h.SeedBasicsAsync();
            var supplier = await Db.Suppliers.SingleAsync();
            supplier.ApprovalStatus = ApprovalStatus.Pending;
            await Db.SaveChangesAsync();
            await Volunteers().AddRoleAsync(supplier.UserId, VolunteerForm());

            var result = await LoginAsync("supplier@test");

            Assert.Equal(AuthError.SupplierPendingApproval, result.Error);
        }

        [Fact]
        public async Task Admin_CannotAddRoles()
        {
            await _h.SeedBasicsAsync();

            Assert.Equal(AddRoleResult.NotAvailableForAdmin, await Volunteers().AddRoleAsync(_h.AdminUser.Id, VolunteerForm()));
            Assert.False((await Users().GetMyProfileAsync(_h.AdminUser.Id))!.CanAddRoles);
            Assert.Empty(await Db.Volunteers.ToListAsync());
        }

        [Fact]
        public async Task Volunteer_AddsNgoRole_WithTheSameValidationAsRegistration()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var form = new CreateNgoDto
            {
                NgoName = "עמותת חסד", DailyCapacity = 50, Address = "רחוב העמותה 5", RegistrationNumber = "580000001",
                RegionId = _h.Region.Id, ReceivesOnSaturdayNight = true,
                SaturdayNightOpenTime = new TimeSpan(12, 0, 0), SaturdayNightCloseTime = new TimeSpan(15, 0, 0)
            };

            await Assert.ThrowsAsync<ArgumentException>(() => Ngos().AddRoleAsync(volunteer.UserId, form));   // מוצ"ש לפני 17:00

            form.SaturdayNightOpenTime = new TimeSpan(20, 0, 0);
            form.SaturdayNightCloseTime = new TimeSpan(23, 0, 0);
            Assert.Equal(AddRoleResult.Added, await Ngos().AddRoleAsync(volunteer.UserId, form));
            Assert.Equal(AddRoleResult.AlreadyHasRole, await Ngos().AddRoleAsync(volunteer.UserId, form));

            _h.Fresh();
            var ngo = await Db.Ngos.SingleAsync(n => n.UserId == volunteer.UserId);
            Assert.Equal(ApprovalStatus.Pending, ngo.ApprovalStatus);
            Assert.Equal("עמותת חסד", ngo.NgoName);
            Assert.Equal(32.09, ngo.Latitude);
            Assert.Equal(new TimeSpan(9, 0, 0), ngo.SunToThuOpenTime);   // ברירת המחדל כמו בהרשמה

            // המתנדב (המאושר) ממשיך להיכנס כרגיל
            var login = await LoginAsync("vol@test");
            Assert.True(login.Succeeded, login.Message);
            Assert.Equal("Volunteer", login.Response!.Role);
        }

        [Fact]
        public async Task AdminUsersList_ShowsAllTheRolesOfAnAccount()
        {
            await _h.SeedBasicsAsync();
            var supplierUser = await Db.Users.SingleAsync(u => u.Email == "supplier@test");
            await Volunteers().AddRoleAsync(supplierUser.Id, VolunteerForm());
            _h.Fresh();

            var page = await Users().GetPageAsync(new PageRequest { Page = 1, PageSize = 50 });

            var row = Assert.Single(page.Items, u => u.Email == "supplier@test");
            Assert.Equal(new[] { "Volunteer", "Supplier" }, row.Roles);
            Assert.Equal(new[] { "Admin" }, Assert.Single(page.Items, u => u.Email == "admin@test").Roles);
        }

        // ---------------- הקונטרולר: תשובות בלי חריגות ----------------

        private static VolunteersController VolunteersAs(int userId, IVolunteerService service)
        {
            var controller = new VolunteersController(service, new Mock<IApprovalRequestService>().Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"))
                    }
                }
            };
            return controller;
        }

        [Theory]
        [InlineData(AddRoleResult.Added, 200)]
        [InlineData(AddRoleResult.AlreadyHasRole, 409)]
        [InlineData(AddRoleResult.NotAvailableForAdmin, 403)]
        public async Task AddRoleEndpoint_MapsTheResult(AddRoleResult result, int expectedStatus)
        {
            var service = new Mock<IVolunteerService>();
            service.Setup(s => s.AddRoleAsync(7, It.IsAny<CreateVolunteerDto>())).ReturnsAsync(result);

            var response = await VolunteersAs(7, service.Object).CreateVolunteer(VolunteerForm());

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(response);
            Assert.Equal(expectedStatus, objectResult.StatusCode ?? 200);
        }
    }
}
