using AutoMapper;
using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Mappings;
using BLL.Notifications;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FoodRescue.Tests
{
    // ---------------------------------------------------------------
    // כלי עזר משותפים לבדיקות: מסד בזיכרון, שעון קבוע, Hangfire מוקלט, מרחקים בלי Google
    // ---------------------------------------------------------------

    public sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public TestClock(DateTime utcNow) => Now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }

    // במקום Hangfire אמיתי: שומר את הג'ובים שהשירותים מבקשים להריץ
    public sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> Jobs { get; } = new();

        public string Create(Job job, IState state)
        {
            Jobs.Add((job, state));
            return Jobs.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;

        public bool Has<T>(string methodName) =>
            Jobs.Any(j => j.Job.Type == typeof(T) && j.Job.Method.Name == methodName);

        public int Count<T>(string methodName) =>
            Jobs.Count(j => j.Job.Type == typeof(T) && j.Job.Method.Name == methodName);
    }

    // מרחקים לפי קו אווירי בלבד (בלי פנייה ל-Google)
    public sealed class EstimateOnlyDistances : IGoogleMapsDistanceService
    {
        private readonly IMatchingPolicyProvider _policy;
        public int Calls { get; private set; }

        public EstimateOnlyDistances(IMatchingPolicyProvider policy) => _policy = policy;

        public Task<(double DistanceKm, double DurationMinutes)> GetDrivingDistanceAsync(double originLat, double originLng, double destLat, double destLng)
        {
            Calls++;
            return Task.FromResult(GoogleMapsDistanceService.Estimate(originLat, originLng, destLat, destLng, _policy.Current));
        }

        public Task<EncodedRoute?> GetRouteAsync(GeoPoint origin, IReadOnlyList<GeoPoint> intermediates, GeoPoint destination) =>
            Task.FromResult<EncodedRoute?>(null);
    }

    public static class TestDb
    {
        public static FoodRescueDbContext New(string? name = null) =>
            new(new DbContextOptionsBuilder<FoodRescueDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
                .AddInterceptors(new RowVersionInterceptor())
                .Options);

        // ב-SQL Server העמודה RowVersion מתמלאת אוטומטית; במסד בזיכרון ממלאים אותה כאן
        private sealed class RowVersionInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
        {
            public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> SavingChanges(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
            {
                Fill(eventData.Context);
                return result;
            }

            public override System.Threading.Tasks.ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                System.Threading.CancellationToken cancellationToken = default)
            {
                Fill(eventData.Context);
                return new(result);
            }

            private static void Fill(DbContext? context)
            {
                if (context == null) return;
                foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                {
                    var property = entry.Metadata.FindProperty("RowVersion");
                    if (property != null && entry.Property("RowVersion").CurrentValue == null)
                        entry.Property("RowVersion").CurrentValue = RandomNumberGenerator.GetBytes(8);
                }
            }
        }

        public static IMapper Mapper { get; } =
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        public static IConfiguration Config(Dictionary<string, string?>? extra = null)
        {
            var data = new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "0123456789abcdef0123456789abcdef-test",
                ["JwtSettings:Issuer"] = "test-issuer",
                ["JwtSettings:Audience"] = "test-audience",
                ["JwtSettings:ExpiresMinutes"] = "30",
                ["App:FrontendBaseUrl"] = "https://app.test"
            };
            if (extra != null)
                foreach (var kv in extra) data[kv.Key] = kv.Value;
            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        public static ILogger<T> Log<T>() => NullLogger<T>.Instance;
    }

    /// <summary>
    /// כל שכבות המנוע והמסלול על מסד בזיכרון: Repositories אמיתיים, שירותים אמיתיים,
    /// בלי Google, בלי SMTP ובלי Hangfire. שעון קבוע: יום שני 19/10/2026 10:00 שעון ישראל.
    /// </summary>
    public sealed class EngineHarness : IDisposable
    {
        public static readonly DateTime LocalNow = new(2026, 10, 19, 10, 0, 0);   // יום שני

        public FoodRescueDbContext Db { get; }
        public TestClock Clock { get; }
        public DateTime NowUtc => Clock.GetUtcNow().UtcDateTime;
        public RecordingJobClient Jobs { get; } = new();
        public StaticMatchingPolicyProvider Policy { get; }
        public IPayloadProtector Protector { get; } = new AesGcmPayloadProtector(RandomNumberGenerator.GetBytes(32));

        public UserRepository Users { get; }
        public DonationRepository Donations { get; }
        public MatchRepository Matches { get; }
        public VolunteerRepository Volunteers { get; }
        public NgoRepository Ngos { get; }
        public SupplierRepository Suppliers { get; }
        public VolunteerRouteRepository Routes { get; }
        public NotificationRepository Notifications { get; }
        public ActionTokenRepository ActionTokenRepo { get; }
        public UnitOfWork UnitOfWork { get; }

        public ActionTokenService ActionTokens { get; }
        public NotificationService NotificationService { get; }
        public MatchCreationService MatchCreation { get; }
        public RouteWorkflowService Workflow { get; }
        public VolunteerReliabilityService Reliability { get; }
        public EngineRunState RunState { get; } = new();
        public DonationMatchingBatchService Engine { get; }
        public CallerProfileService Caller { get; }
        public MatchService MatchService { get; }
        public MatchTimeoutService Timeouts { get; }

        public EngineHarness(MatchingPolicyOptions? policy = null)
        {
            LocalClock.Configure("Asia/Jerusalem");

            Db = TestDb.New();
            Clock = new TestClock(LocalClock.ToUtc(LocalNow));
            Policy = new StaticMatchingPolicyProvider(policy ?? new MatchingPolicyOptions { AutoNoShowGraceMinutes = 30 });
            var config = TestDb.Config();

            Users = new UserRepository(Db);
            Donations = new DonationRepository(Db);
            Matches = new MatchRepository(Db);
            Volunteers = new VolunteerRepository(Db);
            Ngos = new NgoRepository(Db);
            Suppliers = new SupplierRepository(Db);
            Routes = new VolunteerRouteRepository(Db);
            Notifications = new NotificationRepository(Db);
            ActionTokenRepo = new ActionTokenRepository(Db);
            UnitOfWork = new UnitOfWork(Db);
            var regions = new RegionRepository(Db);
            var feedbacks = new FeedbackRepository(Db);

            ActionTokens = new ActionTokenService(ActionTokenRepo);
            NotificationService = new NotificationService(Notifications, Users, Protector, Jobs, new NullRealtimeNotifier(), TestDb.Log<NotificationService>());
            Reliability = new VolunteerReliabilityService(Volunteers, feedbacks, Matches, Policy);

            MatchCreation = new MatchCreationService(Matches, Donations, Routes, Users, UnitOfWork, ActionTokens,
                NotificationService, Jobs, Policy, config, TestDb.Log<MatchCreationService>());

            Workflow = new RouteWorkflowService(Matches, Routes, Volunteers, ActionTokens, NotificationService, UnitOfWork,
                Jobs, Reliability, TestDb.Log<RouteWorkflowService>());

            var distances = new EstimateOnlyDistances(Policy);
            var routeOptimization = new RouteOptimizationService(distances, Policy);

            Engine = new DonationMatchingBatchService(
                Donations, Ngos, Volunteers, regions, Matches,
                new NgoSelectionService(Matches, Policy, TestDb.Log<NgoSelectionService>()),
                new DonationMatchingOrchestrator(new KnapsackSolver(), routeOptimization, Policy, TestDb.Log<DonationMatchingOrchestrator>()),
                MatchCreation,
                new RegionAvailabilityMatchingService(TestDb.Log<RegionAvailabilityMatchingService>(), Policy),
                new VolunteerRankingService(new VolunteerMatchScorer(Policy)),
                new BacktrackingAssignmentService(),
                distances,
                new AdminEscalationService(Users, Notifications, NotificationService, TestDb.Log<AdminEscalationService>()),
                Policy,
                new InProcessEngineLock(),
                RunState,
                TestDb.Log<DonationMatchingBatchService>(),
                Clock);

            Caller = new CallerProfileService(Volunteers, Ngos, Suppliers);
            MatchService = new MatchService(Matches, Donations, Routes, Engine, Workflow, RunState, Caller, ActionTokens,
                Policy, TestDb.Mapper, TestDb.Log<MatchService>());
            Timeouts = new MatchTimeoutService(Matches, Workflow, Policy, TestDb.Log<MatchTimeoutService>());
        }

        // ---------------- נתוני בסיס ----------------

        public Region Region { get; private set; } = null!;
        public User AdminUser { get; private set; } = null!;
        public Supplier Supplier { get; private set; } = null!;
        public Ngo Ngo { get; private set; } = null!;
        public FoodCategory Category { get; private set; } = null!;

        // אזור, מנהל, ספק מאושר, עמותה מאושרת (פתוחה כל היום) וקטגוריה
        public async Task SeedBasicsAsync()
        {
            Region = new Region { RegionName = "מרכז", CenterLatitude = 32.08, CenterLongitude = 34.78, RadiusKm = 20 };
            Db.Regions.Add(Region);

            AdminUser = NewUser("admin@test", UserRole.Admin);
            var supplierUser = NewUser("supplier@test", UserRole.Supplier);
            var ngoUser = NewUser("ngo@test", UserRole.Ngo);
            Db.Users.AddRange(AdminUser, supplierUser, ngoUser);
            await Db.SaveChangesAsync();

            Supplier = new Supplier
            {
                UserId = supplierUser.Id, BusinessName = "מאפיית בדיקה", Address = "רחוב 1", Latitude = 32.080, Longitude = 34.780,
                TaxId = "123456789", GooglePlaceId = "N/A", BusinessLicenseFilePath = "x.pdf", ApprovalStatus = ApprovalStatus.Approved
            };
            Db.Suppliers.Add(Supplier);

            Ngo = NewNgo(ngoUser.Id, ApprovalStatus.Approved, "עמותת בדיקה");
            Db.Ngos.Add(Ngo);

            Category = new FoodCategory { CategoryName = "מאפים", PerishabilityFactor = 1.0 };
            Db.FoodCategories.Add(Category);
            await Db.SaveChangesAsync();
        }

        public Ngo NewNgo(int userId, ApprovalStatus status, string name) => new()
        {
            UserId = userId, NgoName = name, DailyCapacity = 100, Address = "רחוב העמותה 5",
            Latitude = 32.090, Longitude = 34.790, GooglePlaceId = "N/A", RegistrationNumber = "580000000",
            RegionId = Region.Id, ApprovalStatus = status,
            SunToThuOpenTime = TimeSpan.Zero, SunToThuCloseTime = new TimeSpan(23, 59, 0),
            ReceivesOnFriday = true, FridayOpenTime = TimeSpan.Zero, FridayCloseTime = new TimeSpan(23, 59, 0)
        };

        public static User NewUser(string email, UserRole role, string? phone = "0501234567") => new()
        {
            Name = email.Split('@')[0], Email = email, Phone = phone ?? "", PasswordHash = PasswordHasher.Hash("Password123!"),
            Role = role, IsEmailVerified = true, SecurityStamp = UserService.NewStamp()
        };

        // מתנדב עם מיקום GPS עדכני ליד הספק, ומשבצת זמינות לכל היום של היום (יום שני) באזור
        public async Task<Volunteer> AddVolunteerAsync(string email, ApprovalStatus status = ApprovalStatus.Approved,
            double capacityKg = 100, double reliability = 3.5, DateTime? licenseExpiry = null, bool withSlot = true)
        {
            var user = NewUser(email, UserRole.Volunteer);
            Db.Users.Add(user);
            await Db.SaveChangesAsync();

            var volunteer = new Volunteer
            {
                UserId = user.Id, VehicleType = "רכב פרטי", MaxCapacityKg = capacityKg, IsAvailable = true,
                CurrentLatitude = 32.081, CurrentLongitude = 34.781, LocationUpdatedAt = NowUtc.AddMinutes(-1),
                IdNumber = "000000018", DriverLicenseNumber = "1234567", DriverLicenseExpiry = licenseExpiry ?? NowUtc.AddYears(2),
                DateOfBirth = new DateTime(1990, 1, 1), ReliabilityScore = reliability, ApprovalStatus = status,
                RowVersion = new byte[8]
            };
            if (withSlot)
            {
                volunteer.AvailabilitySlots = new List<AvailabilitySlot>
                {
                    new() { RegionId = Region.Id, Day = LocalNow.DayOfWeek, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0) }
                };
            }
            Db.Volunteers.Add(volunteer);
            await Db.SaveChangesAsync();
            return volunteer;
        }

        // תרומה ממתינה של היום: נוצרה לפני 10 דקות, איסוף עד 3 שעות מעכשיו, תפוגה בעוד 8 שעות
        public async Task<Donation> AddDonationAsync(double weightKg = 5, double latitude = 32.0805, double longitude = 34.7805,
            TimeSpan? pickupIn = null, TimeSpan? expiresIn = null)
        {
            var donation = new Donation
            {
                SupplierId = Supplier.Id, RegionId = Region.Id, Latitude = latitude, Longitude = longitude,
                GooglePlaceId = "N/A", ImageFilePath = "", CreatedAt = NowUtc.AddMinutes(-10),
                LatestPickupTime = (LocalNow + (pickupIn ?? TimeSpan.FromHours(3))).TimeOfDay,
                ExpirationTime = NowUtc + (expiresIn ?? TimeSpan.FromHours(8)),
                Status = DonationStatus.Pending, PriorityScore = 1, UrgencyScore = 1, PriorityUpdatedAt = NowUtc,
                RowVersion = new byte[8],
                DonationFoodCategories = new List<DonationFoodCategory>
                {
                    new() { CategoryId = Category.Id, Quantity = 2, WeightKg = weightKg }
                }
            };
            Db.Donations.Add(donation);
            await Db.SaveChangesAsync();
            return donation;
        }

        // המסד בזיכרון מחזיק את אותם אובייקטים - מנקים את המעקב כדי לקרוא מצב "טרי"
        public void Fresh() => Db.ChangeTracker.Clear();

        public void Dispose() => Db.Dispose();
    }
}
