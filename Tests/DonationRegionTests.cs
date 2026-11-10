using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Services;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 33: הספק לא מזין כתובת איסוף ולא בוחר אזור - הכול נלקח מהכתובת שלו מההרשמה
    public sealed class DonationRegionTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private static Region R(int id, string name, double lat, double lng, double radiusKm) =>
            new() { Id = id, RegionName = name, CenterLatitude = lat, CenterLongitude = lng, RadiusKm = radiusKm };

        private static readonly Region Jerusalem = R(1, "ירושלים", 31.7683, 35.2137, 20);
        private static readonly Region TelAviv = R(2, "תל אביב והמרכז", 32.0853, 34.7818, 20);
        private static readonly Region Sharon = R(3, "השרון", 32.3215, 34.8532, 20);
        private static readonly Region Shfela = R(4, "השפלה", 31.8928, 34.8113, 20);

        [Fact]
        public void Locate_PicksTheRegionThePointIsIn()
        {
            var regions = new[] { Jerusalem, TelAviv, Sharon, Shfela };

            Assert.Equal("תל אביב והמרכז", RegionLocator.Locate(regions, 32.0809, 34.7806)!.RegionName);   // דיזנגוף
            Assert.Equal("ירושלים", RegionLocator.Locate(regions, 31.7767, 35.2345)!.RegionName);          // העיר העתיקה
            Assert.Equal("השרון", RegionLocator.Locate(regions, 32.3290, 34.8566)!.RegionName);             // נתניה
        }

        [Fact]
        public void Locate_WhenRegionsOverlap_PicksTheOneThePointIsMostInsideOf()
        {
            // רמת גן נמצאת בתוך הרדיוס גם של "תל אביב והמרכז" וגם של "השפלה" - אבל הרבה יותר קרובה למרכז של תל אביב
            var regions = new[] { Shfela, TelAviv };
            Assert.Equal("תל אביב והמרכז", RegionLocator.Locate(regions, 32.0684, 34.8248)!.RegionName);
        }

        [Fact]
        public void Locate_OutsideAllRegions_PicksTheNearestCenter_AndWithoutRegionsReturnsNull()
        {
            var regions = new[] { Jerusalem, TelAviv };
            Assert.Equal("ירושלים", RegionLocator.Locate(regions, 31.2530, 34.7915)!.RegionName);          // באר שבע - מחוץ לשניהם
            Assert.Null(RegionLocator.Locate(Array.Empty<Region>(), 32, 34.8));
        }

        // ---------------- יצירת תרומה ----------------

        private DonationService Donations() => new(
            _h.Donations, _h.Suppliers, _h.Ngos, _h.Matches, _h.Caller,
            new Mock<IImageService>().Object, new Mock<IImageAnalysisService>().Object,
            new FoodCategoryRepository(_h.Db), new RegionRepository(_h.Db),
            _h.ActionTokens, _h.Workflow, _h.UnitOfWork, _h.Policy, TestDb.Mapper);

        // שעת איסוף עתידית לפי השעון האמיתי (השירות משווה ל-DateTime.UtcNow). null אם קרוב מדי לחצות.
        private static TimeSpan? PickupLaterToday()
        {
            var local = LocalClock.ToLocal(DateTime.UtcNow);
            if (local.TimeOfDay > new TimeSpan(23, 0, 0)) return null;
            return new TimeSpan(local.Hour + 1 > 23 ? 23 : local.Hour + 1, 30, 0);
        }

        private CreateDonationDto Dto(TimeSpan pickup, int? regionId = null) => new()
        {
            RegionId = regionId, LatestPickupTime = pickup,
            Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 2, WeightKg = 3 } }
        };

        private async Task<Region> AddFarRegionAsync()
        {
            var far = new Region { RegionName = "באר שבע והדרום", CenterLatitude = 31.2520, CenterLongitude = 34.7915, RadiusKm = 35 };
            _h.Db.Regions.Add(far);
            await _h.Db.SaveChangesAsync();
            return far;
        }

        [Fact]
        public async Task Supplier_TheRegionComesFromTheBusinessAddress_NotFromTheForm()
        {
            await _h.SeedBasicsAsync();     // הספק בתל אביב, האזור "מרכז"
            var far = await AddFarRegionAsync();
            var pickup = PickupLaterToday();
            if (pickup == null) return;

            // גם בלי אזור, וגם עם אזור "שגוי" (לקוח ישן) - האזור נקבע לפי המיקום של העסק
            var withoutRegion = await Donations().CreateAsync(Dto(pickup.Value), _h.Supplier.UserId);
            var withWrongRegion = await Donations().CreateAsync(Dto(pickup.Value, far.Id), _h.Supplier.UserId);

            _h.Fresh();
            var saved = await _h.Db.Donations.OrderBy(d => d.Id).ToListAsync();
            Assert.All(saved, d => Assert.Equal(_h.Region.Id, d.RegionId));
            Assert.All(saved, d => Assert.Equal((_h.Supplier.Latitude, _h.Supplier.Longitude), (d.Latitude, d.Longitude)));
            Assert.Equal(_h.Region.Id, withoutRegion.RegionId);
            Assert.Equal(_h.Region.Id, withWrongRegion.RegionId);
        }

        [Fact]
        public async Task Admin_CreatingForASupplier_CanStillSetTheRegion()
        {
            await _h.SeedBasicsAsync();
            var far = await AddFarRegionAsync();
            var pickup = PickupLaterToday();
            if (pickup == null) return;

            var created = await Donations().CreateForSupplierAsync(_h.Supplier.Id, Dto(pickup.Value, far.Id), _h.AdminUser.Id);

            Assert.Equal(far.Id, created.RegionId);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Donations().CreateForSupplierAsync(_h.Supplier.Id, Dto(pickup.Value, 999), _h.AdminUser.Id));
        }
    }
}
