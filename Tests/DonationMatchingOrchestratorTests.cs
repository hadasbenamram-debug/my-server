using BLL.Algorithms;
using BLL.Configuration;
using DAL;
using DAL.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תוקן: הבנאי קיבל 3 פרמטרים בטסטים הישנים והמתודה שונתה לגרסה אסינכרונית - הטסטים לא עברו קומפילציה.
    // כל הזמנים כאן מוגדרים בשעון ישראל ומומרים ל-UTC (כמו במערכת עצמה).
    public class DonationMatchingOrchestratorTests
    {
        // יום שני, 09:00 שעון מקומי
        private static readonly DateTime LocalStart = new(2026, 10, 19, 9, 0, 0);
        private static DateTime Utc(DateTime local) => LocalClock.ToUtc(local);
        private static readonly DateTime StartUtc;

        static DonationMatchingOrchestratorTests()
        {
            LocalClock.Configure("Asia/Jerusalem");
            StartUtc = Utc(LocalStart);
        }

        private static Donation MakeDonation(int id, double lat, double lon, TimeSpan latestPickupAfterStart, double weightKg = 1.0) => new()
        {
            Id = id,
            CreatedAt = StartUtc.AddMinutes(-5),
            Latitude = lat,
            Longitude = lon,
            RegionId = 1,
            LatestPickupTime = (LocalStart + latestPickupAfterStart).TimeOfDay,
            ExpirationTime = StartUtc + TimeSpan.FromHours(6),
            DonationFoodCategories = new List<DonationFoodCategory>
            {
                new() { CategoryId = 1, WeightKg = weightKg, Quantity = 1 }
            },
            Status = DonationStatus.Pending,
            PriorityScore = 1.0
        };

        private static Volunteer MakeVolunteer(double capacityKg, TimeSpan? slotEnd = null) => new()
        {
            Id = 1,
            MaxCapacityKg = capacityKg,
            AvailabilitySlots = new List<AvailabilitySlot>
            {
                new() { RegionId = 1, Day = LocalStart.DayOfWeek, StartTime = TimeSpan.Zero, EndTime = slotEnd ?? new TimeSpan(23, 59, 0) }
            }
        };

        private static Ngo MakeNgo(TimeSpan? close = null) => new()
        {
            Id = 1, RegionId = 1, Latitude = 32.0, Longitude = 34.0,
            SunToThuOpenTime = TimeSpan.Zero, SunToThuCloseTime = close ?? new TimeSpan(23, 59, 0)
        };

        // שירות מסלול מדומה: כל תחנה מגיעה אחרי "minutesPerStop" דקות, והקטע לעמותה לוקח 10 דקות
        private static Mock<IRouteOptimizationService> RouteService(double minutesPerStop)
        {
            var mock = new Mock<IRouteOptimizationService>();
            mock.Setup(s => s.OptimizeRouteWithGoogleMapsAsync(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(),
                    It.IsAny<DateTime>(), It.IsAny<Func<PickupStop, DateTime, bool>?>()))
                .Returns((GeoCoordinate start, List<PickupStop> stops, DateTime t, Func<PickupStop, DateTime, bool>? ok) =>
                {
                    var eta = t;
                    foreach (var st in stops)
                    {
                        eta = eta.AddMinutes(minutesPerStop);
                        st.EstimatedArrival = eta;
                        st.LegDistanceKm = 1;
                        st.LegDurationMinutes = minutesPerStop;
                    }
                    return Task.FromResult(stops);
                });
            mock.Setup(s => s.EstimateLegAsync(It.IsAny<GeoCoordinate>(), It.IsAny<GeoCoordinate>()))
                .Returns(Task.FromResult((1.0, 10.0)));
            return mock;
        }

        private static DonationMatchingOrchestrator Create(Mock<IRouteOptimizationService> routeService) =>
            new(new KnapsackSolver(), routeService.Object, new StaticMatchingPolicyProvider(),
                NullLogger<DonationMatchingOrchestrator>.Instance);

        private static readonly GeoCoordinate Start = new() { Latitude = 32.0, Longitude = 34.0 };

        [Fact]
        public async Task PassingCase_ReturnsRoute_WithNgoArrivalAndTotals()
        {
            var d1 = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(2));

            var plan = await Create(RouteService(30)).BuildRouteAsync(MakeVolunteer(50), new List<Donation> { d1 }, Start, MakeNgo(), StartUtc);

            Assert.NotNull(plan);
            Assert.Contains(plan!.Stops, s => s.DonationId == d1.Id);
            Assert.True(plan.NgoArrival > plan.Stops.Last().EstimatedArrival);
            Assert.Equal(1.0, plan.TotalWeightKg, 3);
            Assert.True(plan.TotalDistanceKm > 0);
        }

        [Fact]
        public async Task FarAwayDonation_IsNotPartOfTheRoute()
        {
            var compatible = MakeDonation(1, 32.0, 34.0, TimeSpan.FromHours(2));
            var farAway = MakeDonation(2, 40.0, 40.0, TimeSpan.FromHours(2));

            var stops = await Create(RouteService(30)).ProcessVolunteerRouteAssignmentAsync(
                MakeVolunteer(50), new List<Donation> { compatible, farAway }, Start, MakeNgo(), StartUtc);

            Assert.Contains(stops, s => s.DonationId == compatible.Id);
            Assert.DoesNotContain(stops, s => s.DonationId == farAway.Id);
        }

        [Fact]
        public async Task CapacityIsRespected_ByTheKnapsack()
        {
            var d1 = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(3), weightKg: 8);
            var d2 = MakeDonation(2, 32.002, 34.002, TimeSpan.FromHours(3), weightKg: 8);

            var plan = await Create(RouteService(10)).BuildRouteAsync(MakeVolunteer(10), new List<Donation> { d1, d2 }, Start, MakeNgo(), StartUtc);

            Assert.NotNull(plan);
            Assert.Single(plan!.Stops);
            Assert.True(plan.TotalWeightKg <= 10);
        }

        [Fact]
        public async Task NgoClosedAtArrival_ReturnsNoRoute()
        {
            var d1 = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(3));
            // העמותה נסגרת ב-09:30, וההגעה אליה רק אחרי 60+5+10 דקות
            var ngo = MakeNgo(close: new TimeSpan(9, 30, 0));

            var plan = await Create(RouteService(60)).BuildRouteAsync(MakeVolunteer(50), new List<Donation> { d1 }, Start, ngo, StartUtc);

            Assert.Null(plan);
        }

        [Fact]
        public async Task PickupDeadlineBeforeEta_ReturnsNoRoute()
        {
            var d1 = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(1));
            var orchestrator = Create(RouteService(90));   // ETA אחרי 90 דקות, האיסוף האחרון אחרי 60

            var stop = new PickupStop { DonationId = 1, EstimatedArrival = StartUtc.AddMinutes(90) };
            Assert.False(orchestrator.IsWithinPickupDeadline(stop, d1));
            Assert.True(orchestrator.IsWithinPickupDeadline(new PickupStop { DonationId = 1, EstimatedArrival = StartUtc.AddMinutes(30) }, d1));

            Assert.Null(await orchestrator.BuildRouteAsync(MakeVolunteer(50), new List<Donation> { d1 }, Start, MakeNgo(), StartUtc));
        }

        [Fact]
        public async Task VolunteerAvailabilityMustCoverTheWholeRoute()
        {
            var d1 = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(3));
            // המתנדב זמין עד 09:30 בלבד; המסלול מסתיים אחרי 09:30
            var volunteer = MakeVolunteer(50, slotEnd: new TimeSpan(9, 30, 0));
            var orchestrator = Create(RouteService(60));

            Assert.False(orchestrator.VolunteerAvailabilityCoversRoute(volunteer, StartUtc, StartUtc.AddMinutes(75), new List<Donation> { d1 }, MakeNgo()));
            Assert.True(orchestrator.VolunteerAvailabilityCoversRoute(volunteer, StartUtc, StartUtc.AddMinutes(20), new List<Donation> { d1 }, MakeNgo()));

            Assert.Null(await orchestrator.BuildRouteAsync(volunteer, new List<Donation> { d1 }, Start, MakeNgo(), StartUtc));
        }

        [Fact]
        public async Task AnchorDonation_MustBeInTheRoute()
        {
            var anchor = MakeDonation(1, 32.001, 34.001, TimeSpan.FromHours(3));
            var other = MakeDonation(2, 32.002, 34.002, TimeSpan.FromHours(3));

            var plan = await Create(RouteService(10)).BuildRouteAsync(MakeVolunteer(50), new List<Donation> { anchor, other }, Start, MakeNgo(), StartUtc, anchorDonationId: 1);

            Assert.NotNull(plan);
            Assert.Contains(plan!.Stops, s => s.DonationId == 1);
        }
    }
}
