using BLL.Algorithms;
using DAL.Classes;
using DAL;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace FoodRescue.Tests
{
    public class DonationMatchingOrchestratorTests
    {
        private Donation MakeDonation(int id, double lat, double lon, DateTime createdAt, TimeSpan latestPickupOffset)
        {
            return new Donation
            {
                Id = id,
                CreatedAt = createdAt,
                Latitude = lat,
                Longitude = lon,
                RegionId = 1,
                LatestPickupTime = (createdAt + latestPickupOffset).TimeOfDay,
                ExpirationTime = createdAt + TimeSpan.FromHours(6),
                DonationFoodCategories = new List<DonationFoodCategory>
                {
                    new DonationFoodCategory { CategoryId = 1, WeightKg = 1.0, Quantity = 1 }
                },
                Status = DonationStatus.Pending,
                PriorityScore = 1.0
            };
        }

        private Volunteer MakeVolunteer(int id, double capacityKg)
        {
            return new Volunteer
            {
                Id = id,
                MaxCapacityKg = capacityKg,
                AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = 1, Day = DateTime.UtcNow.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(23) } }
            };
        }

        private Ngo MakeOpenNgo()
        {
            return new Ngo { Id = 1, RegionId = 1, Latitude = 32.0, Longitude = 34.0, SunToThuOpenTime = TimeSpan.FromHours(0), SunToThuCloseTime = TimeSpan.FromHours(23) };
        }

        [Fact]
        public void Orchestrator_PassingCase_ReturnsRoute()
        {
            // test label
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var d1 = MakeDonation(1, 32.001, 34.001, asOf, TimeSpan.FromHours(2));
            var volunteer = MakeVolunteer(1, 50);
            var ngo = MakeOpenNgo();

            var mockRoute = new List<PickupStop>
            {
                new PickupStop { DonationId = 1, Location = new GeoCoordinate { Latitude = d1.Latitude, Longitude = d1.Longitude }, EstimatedArrival = asOf.AddMinutes(30) }
            };

            var mockRouteSvc = new Mock<IRouteOptimizationService>();
            mockRouteSvc.Setup(s => s.OptimizeRoute(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(), It.IsAny<DateTime>())).Returns(mockRoute);

            var loggerMock = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.DonationMatchingOrchestrator>>();
            var orchestrator = new DonationMatchingOrchestrator(new KnapsackSolver(), mockRouteSvc.Object, loggerMock.Object);

            // Act
            var route = orchestrator.ProcessVolunteerRouteAssignment(volunteer, new List<Donation> { d1 }, new GeoCoordinate { Latitude = 32.0, Longitude = 34.0 }, ngo, asOf);

            // Assert
            Assert.NotNull(route);
            Assert.NotEmpty(route);
            Assert.Contains(route, s => s.DonationId == d1.Id);
        }

        [Fact]
        public void Orchestrator_SingleNgoFiltering_ExcludesIncompatibleDonations()
        {
            // test label
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var compatible = MakeDonation(1, 32.0, 34.0, asOf, TimeSpan.FromHours(2));
            var incompatible = MakeDonation(2, 40.0, 40.0, asOf, TimeSpan.FromHours(2));
            var volunteer = MakeVolunteer(1, 50);
            var ngo = MakeOpenNgo();

            var mockRouteSvc = new Mock<IRouteOptimizationService>();
            mockRouteSvc.Setup(s => s.OptimizeRoute(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(), It.IsAny<DateTime>()))
                .Returns((GeoCoordinate start, List<PickupStop> stops, DateTime t) => {
                    // Ensure returned stops contain deterministic ETAs so orchestrator checks can run
                    var eta = t.AddMinutes(30);
                    foreach (var st in stops)
                        st.EstimatedArrival = eta;
                    return stops;
                });

            var loggerMock = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.DonationMatchingOrchestrator>>();
            var orchestrator = new DonationMatchingOrchestrator(new KnapsackSolver(), mockRouteSvc.Object, loggerMock.Object);

            // Act
            var route = orchestrator.ProcessVolunteerRouteAssignment(volunteer, new List<Donation> { compatible, incompatible }, new GeoCoordinate { Latitude = 32.0, Longitude = 34.0 }, ngo, asOf);

            // Assert: only compatible donation appears
            Assert.NotNull(route);
            Assert.All(route, s => Assert.NotEqual(incompatible.Id, s.DonationId));
            Assert.Contains(route, s => s.DonationId == compatible.Id);
        }

        [Fact]
        public void Orchestrator_NgoClosedAtArrival_ReturnsEmpty()
        {
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var d1 = MakeDonation(1, 32.001, 34.001, asOf, TimeSpan.FromHours(1));
            var volunteer = MakeVolunteer(1, 50);
            var ngo = new Ngo { Id = 1, RegionId = 1, Latitude = 32.0, Longitude = 34.0, SunToThuOpenTime = TimeSpan.FromHours(0), SunToThuCloseTime = TimeSpan.FromHours(1) };

            var mockRoute = new List<PickupStop>
            {
                new PickupStop { DonationId = 1, Location = new GeoCoordinate { Latitude = d1.Latitude, Longitude = d1.Longitude }, EstimatedArrival = asOf.AddHours(2) }
            };

            var mockRouteSvc = new Mock<IRouteOptimizationService>();
            mockRouteSvc.Setup(s => s.OptimizeRoute(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(), It.IsAny<DateTime>())).Returns(mockRoute);

            var loggerMock = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.DonationMatchingOrchestrator>>();
            var orchestrator = new DonationMatchingOrchestrator(new KnapsackSolver(), mockRouteSvc.Object, loggerMock.Object);

            // Act
            var route = orchestrator.ProcessVolunteerRouteAssignment(volunteer, new List<Donation> { d1 }, new GeoCoordinate { Latitude = 32.0, Longitude = 34.0 }, ngo, asOf);

            // Assert: NGO closed at arrival -> empty route
            Assert.Empty(route);
        }

        [Fact]
        public void Orchestrator_DonationLatestPickupEarlierThanEta_ReturnsEmpty()
        {
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var d1 = MakeDonation(1, 32.001, 34.001, asOf, TimeSpan.FromHours(1));
            var volunteer = MakeVolunteer(1, 50);
            var ngo = MakeOpenNgo();

            var mockRoute = new List<PickupStop>
            {
                // ETA intentionally after the donation's pickup date/time
                new PickupStop { DonationId = 1, Location = new GeoCoordinate { Latitude = d1.Latitude, Longitude = d1.Longitude }, EstimatedArrival = asOf.AddHours(3) }
            };

            var mockRouteSvc = new Mock<IRouteOptimizationService>();
            mockRouteSvc.Setup(s => s.OptimizeRoute(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(), It.IsAny<DateTime>())).Returns(mockRoute);

            var loggerMock = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.DonationMatchingOrchestrator>>();
            var orchestrator = new DonationMatchingOrchestrator(new KnapsackSolver(), mockRouteSvc.Object, loggerMock.Object);

            // Unit-test the internal pickup-deadline check in isolation via InternalsVisibleTo
            var stop = mockRoute[0];
            var compatibleDonations = new List<Donation> { d1 };
            // When ETA is after pickup deadline the internal check should reject
            var withinDeadline = orchestrator.IsWithinPickupDeadline(stop, d1);
            Assert.False(withinDeadline);

            // Integration-level assertion: pipeline still returns empty route
            var route = orchestrator.ProcessVolunteerRouteAssignment(volunteer, new List<Donation> { d1 }, new GeoCoordinate { Latitude = 32.0, Longitude = 34.0 }, ngo);
            Assert.Empty(route);
        }

        [Fact]
        public void Orchestrator_VolunteerAvailabilityDoesNotCoverRoute_ReturnsEmpty()
        {
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);
            var d1 = MakeDonation(1, 32.001, 34.001, asOf, TimeSpan.FromHours(2));
            var volunteer = new Volunteer
            {
                Id = 1,
                MaxCapacityKg = 50,
                AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = 1, Day = asOf.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(9) } }
            };
            var ngo = MakeOpenNgo();

            var mockRoute = new List<PickupStop>
            {
                new PickupStop { DonationId = 1, Location = new GeoCoordinate { Latitude = d1.Latitude, Longitude = d1.Longitude }, EstimatedArrival = asOf.AddHours(2) }
            };

            var mockRouteSvc = new Mock<IRouteOptimizationService>();
            mockRouteSvc.Setup(s => s.OptimizeRoute(It.IsAny<GeoCoordinate>(), It.IsAny<List<PickupStop>>(), It.IsAny<DateTime>())).Returns(mockRoute);

            var loggerMock = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.DonationMatchingOrchestrator>>();
            var orchestrator = new DonationMatchingOrchestrator(new KnapsackSolver(), mockRouteSvc.Object, loggerMock.Object);

            // Unit-test the internal volunteer-availability check in isolation via InternalsVisibleTo
            var routeStart = asOf;
            var routeEnd = mockRoute[0].EstimatedArrival.Value;
            var compatibleDonations = new List<Donation> { d1 };
            var covers = orchestrator.VolunteerAvailabilityCoversRoute(volunteer, routeStart, routeEnd, compatibleDonations, ngo);
            Assert.False(covers);

            // Integration-level assertion: pipeline still returns empty route
            var route = orchestrator.ProcessVolunteerRouteAssignment(volunteer, new List<Donation> { d1 }, new GeoCoordinate { Latitude = 32.0, Longitude = 34.0 }, ngo);
            Assert.Empty(route);
        }
    }
}
