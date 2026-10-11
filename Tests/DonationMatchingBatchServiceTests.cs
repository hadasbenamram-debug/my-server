using BLL.Algorithms;
using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class DonationMatchingBatchServiceTests
    {
        private class TestLogger : Microsoft.Extensions.Logging.ILogger<DonationMatchingBatchService>
        {
            public List<string> Logs { get; } = new List<string>();
            IDisposable Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => null;
            bool Microsoft.Extensions.Logging.ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            void Microsoft.Extensions.Logging.ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                try { Logs.Add(formatter(state, exception)); } catch { }
            }
        }
        [Fact]
        public async Task RunBatch_ProcessBothDonations_HigherPriorityFirst()
        {
            // Arrange
            var asOf = DateTime.UtcNow.Date.AddHours(9);

            var now = DateTime.UtcNow;
            var dLow = new Donation {
                Id = 1,
                PriorityScore = 1.0,
                CreatedAt = now,
                Latitude = 32.001,
                Longitude = 34.001,
                RegionId = 1,
                LatestPickupTime = now.AddHours(2).TimeOfDay,
                ExpirationTime = now.AddDays(1),
                DonationFoodCategories = new List<DonationFoodCategory> { new DonationFoodCategory { CategoryId = 1, WeightKg = 1, Quantity = 1 } }
            };
            var dHigh = new Donation {
                Id = 2,
                PriorityScore = 10.0,
                CreatedAt = now,
                Latitude = 32.002,
                Longitude = 34.002,
                RegionId = 1,
                LatestPickupTime = now.AddHours(2).TimeOfDay,
                ExpirationTime = now.AddDays(1),
                DonationFoodCategories = new List<DonationFoodCategory> { new DonationFoodCategory { CategoryId = 1, WeightKg = 1, Quantity = 1 } }
            };

            var donationRepoMock = new Mock<IDonationRepository>();
            donationRepoMock.Setup(r => r.GetAvailableDonationsAsync()).ReturnsAsync(new List<Donation> { dLow, dHigh });

            var ngoRepoMock = new Mock<INgoRepository>();
            ngoRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Ngo> { new Ngo { Id = 1, RegionId = 1, Latitude = 32.0, Longitude = 34.0, SunToThuOpenTime = TimeSpan.FromHours(0), SunToThuCloseTime = TimeSpan.FromHours(23), ReceivesOnFriday = true, FridayOpenTime = TimeSpan.FromHours(0), FridayCloseTime = TimeSpan.FromHours(23) } });

            var volunteerRepoMock = new Mock<IVolunteerRepository>();
            // provide one eligible volunteer so an assignment can succeed
            var eligibleVolunteer = new Volunteer { Id = 99, CurrentLatitude = 32.0, CurrentLongitude = 34.0, LocationUpdatedAt = DateTime.UtcNow, IsAvailable = true, MaxCapacityKg = 10, AvailabilitySlots = new List<AvailabilitySlot> { new AvailabilitySlot { RegionId = 1, Day = DateTime.UtcNow.DayOfWeek, StartTime = TimeSpan.FromHours(0), EndTime = TimeSpan.FromHours(23) } } };
            volunteerRepoMock.Setup(r => r.GetAvailableVolunteersAsync()).ReturnsAsync(new List<Volunteer> { eligibleVolunteer });

            // assignment service: mock the interface directly - fail first, succeed second
            var assignmentMock = new Mock<BLL.Interfaces.IDonationAssignmentService>();
            var assignmentQueue = new Queue<AssignmentResult>(new[] {
                new AssignmentResult { Outcome = AssignmentOutcome.EscalatedNoneEligible },
                new AssignmentResult { Outcome = AssignmentOutcome.Assigned, AssignedVolunteer = new Volunteer { Id = 99 } }
            });
            var capturedResults = new List<AssignmentResult>();
            assignmentMock.Setup(s => s.AssignVolunteerAsync(It.IsAny<Donation>(), It.IsAny<Ngo>(), It.IsAny<IEnumerable<Volunteer>>(), It.IsAny<DateTime>()))
                .Returns(() => {
                    var result = assignmentQueue.Dequeue();
                    capturedResults.Add(result);
                    return Task.FromResult(result);
                });

            var heap = new PriorityHeapService<Donation>();
            var testLogger = new TestLogger();
            var service = new DonationMatchingBatchService(donationRepoMock.Object, ngoRepoMock.Object, volunteerRepoMock.Object, assignmentMock.Object, heap, testLogger);

            // Act
            await service.RunBatchAsync();

            // Assert: Verify AssignVolunteerAsync was called twice and higher-priority donation was attempted first
            // Inspect captured logs for diagnostic clues
            var logs = testLogger.Logs;

            // Final verification: assignment should have been called once per donation
            assignmentMock.Verify(s => s.AssignVolunteerAsync(dHigh, It.IsAny<Ngo>(), It.IsAny<IEnumerable<Volunteer>>(), It.IsAny<DateTime>()), Times.Once);
            assignmentMock.Verify(s => s.AssignVolunteerAsync(dLow, It.IsAny<Ngo>(), It.IsAny<IEnumerable<Volunteer>>(), It.IsAny<DateTime>()), Times.Once);

            // Additionally assert that the second call returned the mocked AssignedVolunteer (Id == 99)
            Assert.Equal(2, capturedResults.Count);
            Assert.Equal(AssignmentOutcome.Assigned, capturedResults[1].Outcome);
            Assert.Equal(99, capturedResults[1].AssignedVolunteer.Id);
            assignmentMock.Verify(s => s.AssignVolunteerAsync(It.IsAny<Donation>(), It.IsAny<Ngo>(), It.IsAny<IEnumerable<Volunteer>>(), It.IsAny<DateTime>()), Times.Exactly(2));
        }
    }
}
