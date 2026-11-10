using BLL.Services;
using DAL.Classes;
using FoodRescue.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using MatchStatus = DAL.Classes.MatchStatus;

namespace FoodRescue.Tests
{
    // תיקון 31: נתוני "ההשפעה שלנו" בדף הבית - אמיתיים, מהמסד (בעבר: מספרים קבועים בקוד הלקוח)
    public sealed class PublicStatsTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private PublicStatsService Stats(IMemoryCache? cache = null) =>
            new(_h.Donations, _h.Suppliers, _h.Volunteers, _h.Ngos, cache ?? new MemoryCache(new MemoryCacheOptions()), _h.Clock);

        private async Task DeliverAsync(Donation donation, DonationStatus status, DateTime deliveredAtUtc, int volunteerId)
        {
            donation.Status = status;
            _h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteerId,
                Status = MatchStatus.Completed, IsActive = false,
                AssignedAt = deliveredAtUtc.AddHours(-1), PickedUpAt = deliveredAtUtc.AddMinutes(-20), DeliveredAt = deliveredAtUtc
            });
            await _h.Db.SaveChangesAsync();
        }

        [Fact]
        public async Task CountsOnlyApprovedPartners_AndOnlyFoodThatReachedAnNgo()
        {
            await _h.SeedBasicsAsync();                                        // ספק מאושר + עמותה מאושרת
            var volunteer = await _h.AddVolunteerAsync("approved@test");
            await _h.AddVolunteerAsync("pending@test", ApprovalStatus.Pending);

            var pendingSupplierUser = EngineHarness.NewUser("pending-supplier@test", UserRole.Supplier);
            _h.Db.Users.Add(pendingSupplierUser);
            await _h.Db.SaveChangesAsync();
            _h.Db.Suppliers.Add(new Supplier
            {
                UserId = pendingSupplierUser.Id, BusinessName = "ממתין", Address = "רחוב 2", TaxId = "123456782",
                BusinessLicenseFilePath = "x.pdf", GooglePlaceId = "N/A", ApprovalStatus = ApprovalStatus.Pending
            });
            await _h.Db.SaveChangesAsync();

            await _h.AddDonationAsync(weightKg: 5);                           // ממתינה - לא נספרת
            var deliveredThisMonth = await _h.AddDonationAsync(weightKg: 7);
            var completedLastMonth = await _h.AddDonationAsync(weightKg: 3);
            await DeliverAsync(deliveredThisMonth, DonationStatus.Delivered, _h.NowUtc.AddHours(-1), volunteer.Id);
            // תחילת החודש לפי שעון ישראל: 2026-10-01 00:00 - המסירה הזו בספטמבר
            await DeliverAsync(completedLastMonth, DonationStatus.Completed,
                BLL.Algorithms.LocalClock.ToUtc(new DateTime(2026, 9, 28, 12, 0, 0)), volunteer.Id);

            var stats = await Stats().GetAsync();

            Assert.Equal(2, stats.DonationsDelivered);
            Assert.Equal(10, stats.KgRescued, 3);
            Assert.Equal(7, stats.KgRescuedThisMonth, 3);
            Assert.Equal(1, stats.PartnerBusinesses);
            Assert.Equal(1, stats.ActiveVolunteers);
            Assert.Equal(1, stats.PartnerNgos);
            Assert.Equal(_h.NowUtc, stats.GeneratedAt);
        }

        [Fact]
        public async Task IsCached_SoAPublicPageDoesNotHitTheDatabaseOnEveryView()
        {
            await _h.SeedBasicsAsync();
            var cache = new MemoryCache(new MemoryCacheOptions());

            var first = await Stats(cache).GetAsync();
            await _h.AddVolunteerAsync("joined-later@test");

            Assert.Equal(first.ActiveVolunteers, (await Stats(cache).GetAsync()).ActiveVolunteers);       // מהמטמון
            Assert.Equal(first.ActiveVolunteers + 1, (await Stats().GetAsync()).ActiveVolunteers);        // מהמסד
        }

        [Fact]
        public void TheEndpointIsOpenToGuests()
        {
            var action = typeof(StatsController).GetMethod(nameof(StatsController.GetPublicStats))!;

            Assert.Single(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
            Assert.Empty(typeof(StatsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        }
    }
}
