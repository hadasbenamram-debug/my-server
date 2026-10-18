using BLL.Algorithms;
using DAL;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FoodRescue.Tests
{
    public class DonationMatchingTests
    {
        // יום שני 09:00 שעון ישראל - זמן קבוע כדי שהבדיקה לא תלויה בשעה שבה היא רצה
        private static readonly DateTime LocalAsOf = new(2026, 10, 19, 9, 0, 0);

        private static (Donation Donation, Ngo Ngo, DateTime AsOfUtc) Setup()
        {
            LocalClock.Configure("Asia/Jerusalem");
            var asOf = LocalClock.ToUtc(LocalAsOf);
            var donation = new Donation
            {
                CreatedAt = asOf,
                Latitude = 32.0,
                Longitude = 34.0,
                RegionId = 1,
                LatestPickupTime = (LocalAsOf + TimeSpan.FromHours(2)).TimeOfDay,
                ExpirationTime = asOf + TimeSpan.FromHours(6),
                DonationFoodCategories = new List<DonationFoodCategory>()
            };
            var ngo = new Ngo { Id = 1, RegionId = 1, Latitude = 32.01, Longitude = 34.01, SunToThuOpenTime = TimeSpan.FromHours(8), SunToThuCloseTime = TimeSpan.FromHours(20) };
            return (donation, ngo, asOf);
        }

        private static Volunteer Volunteer(int id, double lat, double lng, DateTime? locationAt, int? toRegionId = null) => new()
        {
            Id = id, CurrentLatitude = lat, CurrentLongitude = lng, LocationUpdatedAt = locationAt, IsAvailable = true,
            AvailabilitySlots = new List<AvailabilitySlot>
            {
                new() { RegionId = 1, ToRegionId = toRegionId, Day = LocalAsOf.DayOfWeek, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 0, 0) }
            }
        };

        [Fact]
        public void RegionMatching_FilterNearFar_Volunteers()
        {
            var (donation, ngo, asOf) = Setup();
            var near = Volunteer(1, 32.0005, 34.0005, asOf);
            var far = Volunteer(2, 35.0, 37.0, asOf);

            var candidates = new RegionAvailabilityMatchingService().FindCandidates(donation, ngo, new[] { near, far }, asOf);

            Assert.Contains(candidates, v => v.Id == near.Id);
            Assert.DoesNotContain(candidates, v => v.Id == far.Id);
        }

        // מתנדב בלי GPS עדכני נשאר מועמד לפי הזמינות שהצהיר; המנוע מחשב את המרחק ממרכז אזור המוצא שלו
        [Fact]
        public void RegionMatching_MissingGps_FallsBackToTheDeclaredAvailability()
        {
            var (donation, ngo, asOf) = Setup();
            var missing = Volunteer(3, 0, 0, null);

            Assert.Single(new RegionAvailabilityMatchingService().FindCandidates(donation, ngo, new[] { missing }, asOf));
        }

        [Fact]
        public void RegionMatching_NotAvailableNow_Excluded()
        {
            var (donation, ngo, asOf) = Setup();
            var volunteer = Volunteer(6, 32.0005, 34.0005, asOf);
            volunteer.AvailabilitySlots.Single().Day = DayOfWeek.Wednesday;   // היום יום שני

            Assert.Empty(new RegionAvailabilityMatchingService().FindCandidates(donation, ngo, new[] { volunteer }, asOf));
        }

        [Fact]
        public void RegionMatching_DestinationRegion_AllowsDeliveryToAnotherRegion_OnlyWhenDeclared()
        {
            var (donation, _, asOf) = Setup();
            // העמותה באזור 2, התרומה באזור 1
            var ngoInRegion2 = new Ngo { Id = 2, RegionId = 2, Latitude = 32.01, Longitude = 34.01, SunToThuOpenTime = TimeSpan.FromHours(8), SunToThuCloseTime = TimeSpan.FromHours(20) };
            var traveler = Volunteer(4, 32.0005, 34.0005, asOf, toRegionId: 2);
            var local = Volunteer(5, 32.0005, 34.0005, asOf);

            var candidates = new RegionAvailabilityMatchingService().FindCandidates(donation, ngoInRegion2, new[] { traveler, local }, asOf);

            Assert.Contains(candidates, v => v.Id == traveler.Id);
            Assert.DoesNotContain(candidates, v => v.Id == local.Id);
        }
    }
}
