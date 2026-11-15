using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Security;
using BLL.Services;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 82: אזור העמותה נקבע לפי הכתובת שלה, ולא לפי בחירה ידנית (שברירת המחדל שלה הייתה "ירושלים")
    public sealed class NgoRegionTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        [Fact]
        public async Task SavingTheHours_MovesTheNgoToTheRegionOfItsAddress()
        {
            await _h.SeedBasicsAsync();   // אזור "מרכז" (תל אביב) והעמותה בתוכו

            var jerusalem = new Region { RegionName = "ירושלים", CenterLatitude = 31.7683, CenterLongitude = 35.2137, RadiusKm = 20 };
            _h.Db.Regions.Add(jerusalem);
            await _h.Db.SaveChangesAsync();

            // כמו אצל העמותות מפתח תקווה: רשומה בירושלים, הכתובת במרכז
            _h.Ngo.RegionId = jerusalem.Id;
            await _h.Db.SaveChangesAsync();

            var geocoding = new Mock<IGeocodingService>();
            geocoding.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodeResult(32.0870, 34.8875, "חנקין 1, פתח תקווה", "place-id"));   // פתח תקווה

            var service = new NgoService(_h.Ngos, _h.Users, new RegionRepository(_h.Db), new Mock<IApprovalRequestService>().Object,
                _h.Caller, _h.UnitOfWork, _h.NotificationService, TestDb.Mapper, TestDb.Log<NgoService>(),
                new PasswordHasherWithLegacySupport(), geocoding.Object,
                new RegistrationNoticeService(_h.NotificationService, _h.Notifications, TestDb.Config()));

            await service.UpdateMyHoursAsync(_h.Ngo.UserId, new UpdateNgoHoursDto
            {
                Address = "חנקין 1 פתח תקווה",
                SunToThuOpenTime = new TimeSpan(9, 0, 0),
                SunToThuCloseTime = new TimeSpan(23, 59, 0)
            });

            _h.Fresh();
            var ngo = await _h.Db.Ngos.SingleAsync(n => n.Id == _h.Ngo.Id);
            Assert.Equal(_h.Region.Id, ngo.RegionId);
        }
    }
}
