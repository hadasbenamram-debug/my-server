using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Interfaces;
using DAL.Repositories;
using FoodRescue.DTOs;
using FoodRescue.DTOs.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class ValidationAndMappingTests
    {
        // ---------------- AutoMapper ----------------

        [Fact]
        public void MappingProfile_IsValid()
        {
            var config = new AutoMapper.MapperConfiguration(
                cfg => cfg.AddProfile<BLL.Mappings.MappingProfile>(),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            config.AssertConfigurationIsValid();
        }

        // תוקן: UserDto.Username היה תמיד null (בישות השדה נקרא Name)
        [Fact]
        public void User_MapsNameAndRole()
        {
            var dto = TestDb.Mapper.Map<UserDto>(new User { Id = 3, Name = "דנה", Email = "d@x", Role = UserRole.Supplier });
            Assert.Equal("דנה", dto.Name);
            Assert.Equal("Supplier", dto.Role);
        }

        // תוקן: הקטגוריות הוחזרו בלי Id
        [Fact]
        public async Task Categories_AreReturnedWithId()
        {
            using var db = TestDb.New();
            db.FoodCategories.AddRange(new FoodCategory { CategoryName = "ירקות" }, new FoodCategory { CategoryName = "מאפים" });
            await db.SaveChangesAsync();

            var list = await new FoodCategoryService(new FoodCategoryRepository(db), TestDb.Mapper).GetAllAsync();

            Assert.Equal(2, list.Count);
            Assert.All(list, c => Assert.True(c.Id > 0));
            Assert.Equal("ירקות", list.First().CategoryName);   // ממוין לפי שם
        }

        [Fact]
        public void AvailabilitySlot_MapsRegionNamesAndTimes()
        {
            var dto = TestDb.Mapper.Map<AvailabilitySlotResponseDto>(new AvailabilitySlot
            {
                Id = 1, RegionId = 1, Region = new Region { RegionName = "מרכז" }, ToRegionId = 2, ToRegion = new Region { RegionName = "צפון" },
                Day = DayOfWeek.Monday, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(12, 30, 0)
            });

            Assert.Equal("מרכז", dto.RegionName);
            Assert.Equal("צפון", dto.ToRegionName);
            Assert.Equal(1, dto.Day);
            Assert.Equal("08:00", dto.StartTime);
            Assert.Equal("12:30", dto.EndTime);
        }

        // ---------------- אורכי DTO מול המסד ----------------

        // לכל DTO שנכתב למסד: לכל שדה מחרוזת שיש לו אורך מקסימלי בעמודה, חייבת להיות בדיקת אורך ב-DTO,
        // והאורך המותר ב-DTO לא גדול מהעמודה (בעבר: BusinessName 150 מול 100, TaxId 50 מול 20, Region/Category 100 מול 50,
        // ובטופסי ההרשמה לא הייתה בדיקת אורך בכלל).
        public static IEnumerable<object[]> DtoToEntity() => new[]
        {
            new object[] { typeof(RegisterSupplierDto), new[] { typeof(Supplier), typeof(User) } },
            new object[] { typeof(CreateSupplierDto), new[] { typeof(Supplier) } },
            new object[] { typeof(RegisterNgoDto), new[] { typeof(Ngo), typeof(User) } },
            new object[] { typeof(UpdateNgoHoursDto), new[] { typeof(Ngo) } },
            new object[] { typeof(RegisterVolunteerDto), new[] { typeof(Volunteer), typeof(User) } },
            new object[] { typeof(CreateVolunteerDto), new[] { typeof(Volunteer) } },
            new object[] { typeof(UpdateVolunteerDto), new[] { typeof(Volunteer) } },
            new object[] { typeof(RegisterDto), new[] { typeof(User) } },
            new object[] { typeof(UpdateMyProfileDto), new[] { typeof(User) } },
            new object[] { typeof(CreateRegionDto), new[] { typeof(Region) } },
            new object[] { typeof(CreateFoodCategoryDto), new[] { typeof(FoodCategory) } },
            new object[] { typeof(CreateDonationDto), new[] { typeof(Donation) } }
        };

        [Theory]
        [MemberData(nameof(DtoToEntity))]
        public void DtoStringLengths_FitTheDatabaseColumns(Type dtoType, Type[] entityTypes)
        {
            using var db = TestDb.New();
            var problems = new List<string>();

            foreach (var prop in dtoType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                foreach (var entityType in entityTypes)
                {
                    var column = db.Model.FindEntityType(entityType)?.FindProperty(prop.Name);
                    int? dbMax = column?.GetMaxLength();
                    if (dbMax == null) continue;

                    int? dtoMax = prop.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength
                                  ?? prop.GetCustomAttribute<MaxLengthAttribute>()?.Length;

                    if (dtoMax == null)
                        problems.Add($"{dtoType.Name}.{prop.Name}: no length check (DB {entityType.Name} allows {dbMax}).");
                    else if (dtoMax > dbMax)
                        problems.Add($"{dtoType.Name}.{prop.Name}: DTO allows {dtoMax}, DB {entityType.Name} allows {dbMax}.");
                }
            }

            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        // ---------------- תעודת זהות ישראלית ----------------

        [Theory]
        [InlineData("000000018", true)]
        [InlineData("123456782", true)]
        [InlineData("000018", true)]         // פחות מ-9 ספרות - משלימים באפסים
        [InlineData("12345678-2", false)]    // רק ספרות
        [InlineData("123456789", false)]     // ספרת ביקורת שגויה (בעבר עבר כי נבדק רק האורך)
        [InlineData("000000000", false)]
        [InlineData("12345678901", false)]
        [InlineData("abcdefghi", false)]
        public void IsraeliId_ChecksTheCheckDigit(string id, bool valid)
        {
            Assert.Equal(valid, IsraeliId.IsValid(id));
        }

        [Fact]
        public void RegisterVolunteer_RequiresValidId_FutureLicenseExpiry_AndMinimumAge()
        {
            var dto = new RegisterVolunteerDto
            {
                Name = "מתנדב", Email = "v@test.com", Phone = "0501234567", VehicleType = "רכב", MaxCapacityKg = 50,
                IdNumber = "123456789", DriverLicenseNumber = "1234567",
                DriverLicenseExpiry = DateTime.UtcNow.AddDays(-1), DateOfBirth = DateTime.UtcNow.AddYears(-10)
            };

            var errors = Validate(dto).SelectMany(e => e.MemberNames).ToList();

            Assert.Contains(nameof(RegisterVolunteerDto.IdNumber), errors);
            Assert.Contains(nameof(RegisterVolunteerDto.DriverLicenseExpiry), errors);
            Assert.Contains(nameof(RegisterVolunteerDto.DateOfBirth), errors);

            dto.IdNumber = "000000018";
            dto.DriverLicenseExpiry = DateTime.UtcNow.AddYears(1);
            dto.DateOfBirth = DateTime.UtcNow.AddYears(-30);
            Assert.Empty(Validate(dto));
        }

        [Fact]
        public void ValidationService_RejectsExpiredLicense_AndBadId()
        {
            var service = new ValidationService();

            Assert.False(service.ValidateVolunteer("000000018", "1234567", DateTime.UtcNow.AddDays(-3), new DateTime(1990, 1, 1)).Passed);
            Assert.False(service.ValidateVolunteer("123456789", "1234567", DateTime.UtcNow.AddYears(1), new DateTime(1990, 1, 1)).Passed);
            Assert.True(service.ValidateVolunteer("000000018", "1234567", DateTime.UtcNow.AddYears(1), new DateTime(1990, 1, 1)).Passed);
        }

        private static List<ValidationResult> Validate(object dto)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
            return results;
        }

        // ---------------- משבצות זמינות ----------------

        private static async Task<(VolunteerService Service, User User, FoodRescueDbContext Db)> VolunteerServiceAsync()
        {
            var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test", withSlot: false);
            var db = h.Db;
            db.Regions.Add(new Region { RegionName = "צפון", CenterLatitude = 32.8, CenterLongitude = 35, RadiusKm = 30 });
            await db.SaveChangesAsync();

            var service = new VolunteerService(
                h.Volunteers, h.Users, new AvailabilitySlotRepository(db), new RegionRepository(db), h.Routes,
                new Mock<IApprovalRequestService>().Object, new ValidationService(), h.UnitOfWork, h.NotificationService,
                TestDb.Mapper, TestDb.Log<VolunteerService>());

            return (service, await db.Users.SingleAsync(u => u.Id == v.UserId), db);
        }

        private static CreateAvailabilitySlotDto Slot(int region, string start, string end, int? toRegion = null) =>
            new() { RegionId = region, ToRegionId = toRegion, Day = 1, StartTime = start, EndTime = end };

        [Fact]
        public async Task Availability_StartMustBeBeforeEnd()
        {
            var (service, user, db) = await VolunteerServiceAsync();
            using var _ = db;
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SaveMyAvailabilityAsync(user.Id, new List<CreateAvailabilitySlotDto> { Slot(1, "14:00", "10:00") }));
        }

        [Fact]
        public async Task Availability_DestinationRegionMustDifferFromOrigin()
        {
            var (service, user, db) = await VolunteerServiceAsync();
            using var _ = db;
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SaveMyAvailabilityAsync(user.Id, new List<CreateAvailabilitySlotDto> { Slot(1, "08:00", "10:00", toRegion: 1) }));
        }

        // תוקן: אזור שלא קיים החזיר 500
        [Fact]
        public async Task Availability_UnknownRegion_IsABadRequest_NotAServerError()
        {
            var (service, user, db) = await VolunteerServiceAsync();
            using var _ = db;
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SaveMyAvailabilityAsync(user.Id, new List<CreateAvailabilitySlotDto> { Slot(999, "08:00", "10:00") }));
        }

        [Fact]
        public async Task Availability_ValidSlots_AreSaved_WithDestination()
        {
            var (service, user, db) = await VolunteerServiceAsync();
            using var _ = db;
            int north = (await db.Regions.SingleAsync(r => r.RegionName == "צפון")).Id;

            await service.SaveMyAvailabilityAsync(user.Id, new List<CreateAvailabilitySlotDto>
            {
                Slot(1, "08:00", "10:00", toRegion: north),
                Slot(1, "16:00:00", "18:30", toRegion: 0)   // 0 = בלי אזור יעד
            });

            var saved = await service.GetMyAvailabilityAsync(user.Id);
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, s => s.ToRegionId == north && s.ToRegionName == "צפון");
            Assert.Contains(saved, s => s.ToRegionId == null && s.EndTime == "18:30");
        }

        // ---------------- ציון אמינות ----------------

        [Fact]
        public void Reliability_StartsAtThePrior_MovesWithRatings_AndDropsOnNoShow()
        {
            Assert.Equal(3.5, VolunteerReliabilityService.Calculate(Array.Empty<int>(), 0, 0.5), 3);
            Assert.Equal(4.25, VolunteerReliabilityService.Calculate(new[] { 5, 5, 5 }, 0, 0.5), 3);
            Assert.Equal(3.75, VolunteerReliabilityService.Calculate(new[] { 5, 5, 5 }, 1, 0.5), 3);
            Assert.Equal(1.0, VolunteerReliabilityService.Calculate(new[] { 1, 1 }, 10, 0.5), 3);   // לא יורד מתחת ל-1
        }

        // ---------------- api/settings מחובר למנוע ----------------

        [Fact]
        public async Task Settings_AreStoredInTheDatabase_AndReachTheEngine()
        {
            var dbName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.Configure<MatchingPolicyOptions>(o => o.ResponseTimeoutMinutes = 5);
            services.AddDbContext<FoodRescueDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
            services.AddSingleton<IMatchingPolicyProvider, MatchingPolicyProvider>();
            using var sp = services.BuildServiceProvider();

            var provider = sp.GetRequiredService<IMatchingPolicyProvider>();
            await provider.ReloadAsync();
            Assert.Equal(5, provider.Current.ResponseTimeoutMinutes);

            using (var scope = sp.CreateScope())
            {
                var settings = new SettingsService(scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>(), provider);
                var updated = await settings.UpdateAsync(new SystemSettingsDto { MatchingTimeoutMinutes = 12, MaxSearchRadiusKm = 33 }, 1);
                Assert.Equal(12, updated.MatchingTimeoutMinutes);
            }

            // המנוע קורא את המדיניות מהספק - הערכים החדשים בתוקף מיד
            Assert.Equal(12, provider.Current.ResponseTimeoutMinutes);
            Assert.Equal(33, provider.Current.MaxSearchRadiusKm);

            // ונשמרים במסד (שורדים הפעלה מחדש)
            var fresh = new MatchingPolicyProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MatchingPolicyOptions>>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                TestDb.Log<MatchingPolicyProvider>());
            await fresh.ReloadAsync();
            Assert.Equal(12, fresh.Current.ResponseTimeoutMinutes);
        }

        [Fact]
        public async Task Settings_RejectAllZeroWeights()
        {
            using var db = TestDb.New();
            var settings = new SettingsService(new SystemSettingRepository(db), new StaticMatchingPolicyProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => settings.UpdateAsync(new SystemSettingsDto
            {
                DistanceWeight = 0, UrgencyWeight = 0, CapacityWeight = 0, ReliabilityWeight = 0
            }, 1));
        }
    }
}
