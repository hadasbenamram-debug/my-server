using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace FoodRescue.Tests
{
    // מנגנון האישור האחיד: כל נתיבי approve/reject עוברים ב-ApprovalRequestService
    public class NgosControllerAuthorizationTests
    {
        // ---------------- הרשאות ----------------

        [Fact]
        public void RejectNgo_RequiresAdminRole()
        {
            var method = typeof(NgosController).GetMethod(nameof(NgosController.RejectNgo))!;
            Assert.Equal("Admin", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        }

        [Fact]
        public void EveryApproveOrRejectAction_InTheApi_RequiresAdminRole()
        {
            var actions = typeof(NgosController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("Approve") || m.Name.StartsWith("Reject"))
                .ToList();

            Assert.NotEmpty(actions);
            foreach (var m in actions)
            {
                bool adminOnly =
                    m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles?.Contains("Admin") == true) ||
                    m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles?.Contains("Admin") == true);

                Assert.True(adminOnly, $"{m.DeclaringType!.Name}.{m.Name} is not restricted to Admin.");
            }
        }

        [Fact]
        public void Ngo_Listing_IsNotOpenToAnonymous()
        {
            Assert.NotNull(typeof(NgosController).GetCustomAttribute<AuthorizeAttribute>());
            var m = typeof(NgosController).GetMethod(nameof(NgosController.GetNgos))!;
            Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>());
        }

        // תוקן: היו שני מנגנוני אישור (admin/volunteers/{id}/approve בלי מייל). נשאר נתיב אחד לכל ישות.
        [Fact]
        public void DuplicateAdminVolunteerApproveEndpoints_WereRemoved()
        {
            var routes = typeof(NgosController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
                .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => r.Template))
                .ToList();

            Assert.DoesNotContain("api/admin/volunteers", routes);
            Assert.DoesNotContain("api/[controller]", typeof(NgosController).Assembly.GetTypes()
                .Where(t => t.Name == "LookupsController").SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => r.Template)));
        }

        // ---------------- התנהגות (שכבות אמיתיות על מסד בזיכרון) ----------------

        private sealed class Fixture : IDisposable
        {
            public FoodRescueDbContext Db { get; } = TestDb.New();
            public EngineHarness Harness { get; }
            public User Admin { get; private set; } = null!;
            public ApprovalRequestService Approvals { get; }

            public Fixture()
            {
                Harness = new EngineHarness();
                var db = Harness.Db;
                Approvals = new ApprovalRequestService(
                    new ApprovalRequestRepository(db), new VolunteerRepository(db), new SupplierRepository(db), new NgoRepository(db),
                    new ManagerRepository(db), new UserRepository(db), new UnitOfWork(db), new ValidationService(),
                    Harness.NotificationService, TestDb.Config(), TestDb.Mapper, TestDb.Log<ApprovalRequestService>());
            }

            public async Task SeedAsync()
            {
                Admin = EngineHarness.NewUser("admin@test", UserRole.Admin);
                Harness.Db.Users.Add(Admin);
                Harness.Db.Regions.Add(new Region { Id = 1, RegionName = "מרכז", CenterLatitude = 32, CenterLongitude = 34, RadiusKm = 10 });
                await Harness.Db.SaveChangesAsync();
            }

            public async Task<Ngo> AddPendingNgoAsync(string email)
            {
                // הרשמה ישנה: המשתמש נשמר עם Role=User
                var user = EngineHarness.NewUser(email, UserRole.User);
                Harness.Db.Users.Add(user);
                await Harness.Db.SaveChangesAsync();

                var ngo = new Ngo
                {
                    UserId = user.Id, NgoName = "עמותה " + email, DailyCapacity = 10, Address = "א", GooglePlaceId = "N/A",
                    RegistrationNumber = "580000000", RegionId = 1, ApprovalStatus = ApprovalStatus.Pending,
                    SunToThuOpenTime = TimeSpan.FromHours(9), SunToThuCloseTime = TimeSpan.FromHours(17)
                };
                Harness.Db.Ngos.Add(ngo);
                Harness.Db.ApprovalRequests.Add(new ApprovalRequest
                {
                    UserId = user.Id, RequestedRole = RequestedRole.Ngo, Status = ApprovalStatus.Pending,
                    ValidationDetails = "", RejectionReason = ""
                });
                await Harness.Db.SaveChangesAsync();
                return ngo;
            }

            public async Task<Supplier> AddPendingSupplierAsync(string email)
            {
                var user = EngineHarness.NewUser(email, UserRole.Supplier);
                Harness.Db.Users.Add(user);
                await Harness.Db.SaveChangesAsync();

                var supplier = new Supplier
                {
                    UserId = user.Id, BusinessName = "ספק " + email, Address = "א", TaxId = "123456789",
                    GooglePlaceId = "N/A", BusinessLicenseFilePath = "l.pdf", ApprovalStatus = ApprovalStatus.Pending
                };
                Harness.Db.Suppliers.Add(supplier);
                await Harness.Db.SaveChangesAsync();
                return supplier;
            }

            public T AsAdmin<T>(T controller) where T : ControllerBase
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, Admin.Id.ToString()),
                            new Claim(ClaimTypes.Role, "Admin")
                        }, "test"))
                    }
                };
                return controller;
            }

            public NgosController NgoController() =>
                AsAdmin(new NgosController(new Moq.Mock<INgoService>().Object, Approvals));

            public SuppliersController SupplierController() =>
                AsAdmin(new SuppliersController(new Moq.Mock<ISupplierService>().Object, Approvals));

            public void Dispose()
            {
                Harness.Dispose();
                Db.Dispose();
            }
        }

        private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

        [Fact]
        public async Task RejectNgo_UnknownId_IsNotFound()
        {
            using var f = new Fixture();
            await f.SeedAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => f.NgoController().RejectNgo(9, Json("\"reason\"")));
        }

        [Fact]
        public async Task RejectNgo_WithoutReason_IsBadRequest()
        {
            using var f = new Fixture();
            await f.SeedAsync();
            var ngo = await f.AddPendingNgoAsync("ngo@test");

            await Assert.ThrowsAsync<ArgumentException>(() => f.NgoController().RejectNgo(ngo.Id, default));
        }

        // תוקן: דחיית עמותה לא עדכנה את בקשת האישור, ולא נשלח מייל
        [Fact]
        public async Task RejectNgo_UpdatesProfile_AndRequest_AndQueuesEmail()
        {
            using var f = new Fixture();
            await f.SeedAsync();
            var ngo = await f.AddPendingNgoAsync("ngo@test");

            var result = await f.NgoController().RejectNgo(ngo.Id, Json("{\"reason\":\"מסמכים חסרים\"}"));

            Assert.IsType<OkObjectResult>(result);
            f.Harness.Fresh();
            var db = f.Harness.Db;
            Assert.Equal(ApprovalStatus.Rejected, (await db.Ngos.SingleAsync()).ApprovalStatus);

            var request = await db.ApprovalRequests.SingleAsync();
            Assert.Equal(ApprovalStatus.Rejected, request.Status);
            Assert.Equal("מסמכים חסרים", request.RejectionReason);
            Assert.NotNull(request.HandledByManagerId);
            Assert.NotNull(request.HandledAt);

            Assert.Single(await db.Notifications.Where(n => n.UserId == ngo.UserId && n.Channel == NotificationChannel.Email).ToListAsync());
            Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == ngo.UserId)).Role);
        }

        // תוקן: עמותה שאושרה נשארה עם Role=User
        [Fact]
        public async Task ApproveNgo_SetsNgoRole_HandledByManager_AndQueuesEmail()
        {
            using var f = new Fixture();
            await f.SeedAsync();
            var ngo = await f.AddPendingNgoAsync("ngo@test");

            await f.NgoController().ApproveNgo(ngo.Id);

            f.Harness.Fresh();
            var db = f.Harness.Db;
            Assert.Equal(ApprovalStatus.Approved, (await db.Ngos.SingleAsync()).ApprovalStatus);
            Assert.Equal(UserRole.Ngo, (await db.Users.SingleAsync(u => u.Id == ngo.UserId)).Role);

            var request = await db.ApprovalRequests.SingleAsync();
            Assert.Equal(ApprovalStatus.Approved, request.Status);
            var manager = await db.Managers.SingleAsync();
            Assert.Equal(f.Admin.Id, manager.UserId);
            Assert.Equal(manager.Id, request.HandledByManagerId);

            var email = Assert.Single(await db.Notifications.Where(n => n.UserId == ngo.UserId && n.Channel == NotificationChannel.Email).ToListAsync());
            // מייל ההחלטה כולל קישור התחברות רגיל - בלי טוקן
            Assert.DoesNotContain("token=", f.Harness.Protector.Unprotect(email.ProtectedPayload!));
        }

        // תוקן: אישור ספק ניסה גם כמזהה משתמש אם לא נמצא ספק - ויכול היה לאשר ספק אחר
        [Fact]
        public async Task ApproveSupplier_UsesTheSupplierIdOnly()
        {
            using var f = new Fixture();
            await f.SeedAsync();
            var first = await f.AddPendingSupplierAsync("s1@test");
            var second = await f.AddPendingSupplierAsync("s2@test");

            // מזהה שקיים רק כמזהה משתמש (של הספק השני) - לא אמור לאשר אף אחד
            int userIdThatIsNotASupplierId = new[] { second.UserId, first.UserId }.First(id => id != first.Id && id != second.Id);
            await Assert.ThrowsAsync<NotFoundException>(() => f.SupplierController().ApproveSupplier(userIdThatIsNotASupplierId));

            await f.SupplierController().ApproveSupplier(second.Id);

            f.Harness.Fresh();
            var db = f.Harness.Db;
            Assert.Equal(ApprovalStatus.Pending, (await db.Suppliers.SingleAsync(s => s.Id == first.Id)).ApprovalStatus);
            Assert.Equal(ApprovalStatus.Approved, (await db.Suppliers.SingleAsync(s => s.Id == second.Id)).ApprovalStatus);
        }

        [Fact]
        public async Task PendingList_IncludesProfileDetails()
        {
            using var f = new Fixture();
            await f.SeedAsync();
            var ngo = await f.AddPendingNgoAsync("ngo@test");

            var pending = await f.Approvals.GetPendingAsync(FoodRescue.DTOs.RequestedRole.Ngo);

            var item = Assert.Single(pending);
            Assert.Equal(ngo.Id, item.ProfileId);
            Assert.Equal(ngo.NgoName, item.ProfileName);
            Assert.Equal("580000000", item.RegistrationNumber);
        }
    }
}
