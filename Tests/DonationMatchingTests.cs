using BLL.Algorithms;
using DAL.Classes;
using DAL;
using System;
using System.Collections.Generic;
using Xunit;

namespace FoodRescue.Tests
{
    public class DonationMatchingTests
    {
        private Donation CreateDonation(double lat, double lon, TimeSpan latestPickupOffsetFromNow, TimeSpan expiresIn)
        {
            var now = DateTime.UtcNow;
            return new Donation
            {
                CreatedAt = now,
                Latitude = lat,
                Longitude = lon,
                LatestPickupTime = (now + latestPickupOffsetFromNow).TimeOfDay,
                ExpirationTime = now + expiresIn,
                DonationFoodCategories = new List<DonationFoodCategory>(),
                Status = DonationStatus.Pending
            };
        }

        // CreateNgo helper removed (not used in these tests)

        [Fact]
        public void RegionMatching_FilterNearFar_Volunteers()
        {
            var service = new RegionAvailabilityMatchingService();
            // Use a stable morning time so pickupDateTime computation doesn't wrap past midnight
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var donation = new Donation
            {
                CreatedAt = asOf,
                Latitude = 32.0,
                Longitude = 34.0,
                RegionId = 1,
                LatestPickupTime = (asOf + TimeSpan.FromHours(2)).TimeOfDay,
                ExpirationTime = asOf + TimeSpan.FromHours(6)
            };

            var ngo = new Ngo { Id = 1, RegionId = 1, Latitude = 32.01, Longitude = 34.01, SunToThuOpenTime = TimeSpan.FromHours(8), SunToThuCloseTime = TimeSpan.FromHours(20) };

            var near = new Volunteer { Id = 1, CurrentLatitude = 32.0005, CurrentLongitude = 34.0005, LocationUpdatedAt = asOf, IsAvailable = true, AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = donation.RegionId, Day = asOf.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(23) } } };
            var far = new Volunteer { Id = 2, CurrentLatitude = 35.0, CurrentLongitude = 37.0, LocationUpdatedAt = asOf, IsAvailable = true, AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = donation.RegionId, Day = asOf.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(23) } } };

            var candidates = service.FindCandidates(donation, ngo, new[] { near, far }, asOf);

            Assert.Contains(candidates, v => v.Id == near.Id);
            Assert.DoesNotContain(candidates, v => v.Id == far.Id);
        }

        [Fact]
        public void RegionMatching_MissingGps_Excluded()
        {
            var service = new RegionAvailabilityMatchingService();
            var now = DateTime.UtcNow;
            var donation = new Donation
            {
                CreatedAt = now,
                Latitude = 32.0,
                Longitude = 34.0,
                RegionId = 1,
                LatestPickupTime = (now + TimeSpan.FromHours(2)).TimeOfDay,
                ExpirationTime = now + TimeSpan.FromHours(6),
            };

            var ngo = new Ngo { Id = 1, RegionId = 1, Latitude = 32.01, Longitude = 34.01, SunToThuOpenTime = TimeSpan.FromHours(8), SunToThuCloseTime = TimeSpan.FromHours(20) };

            var missing = new Volunteer { Id = 3, CurrentLatitude = 0, CurrentLongitude = 0, LocationUpdatedAt = null, IsAvailable = true, AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = donation.RegionId, Day = now.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(23) } } };

            var candidates = service.FindCandidates(donation, ngo, new[] { missing }, DateTime.UtcNow);

            Assert.Empty(candidates);
        }
    }
}
