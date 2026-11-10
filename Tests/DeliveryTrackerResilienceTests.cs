using AutoMapper;
using BLL.Notifications;
using BLL.Services;
using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 34: למה טבלת DeliveryLogs לא התעדכנה - ה-migration בשם DeliveryLogTracking לא הורץ, וכל רישום ליומן נכשל
    // בשקט. עכשיו: עדכון המיקום של המתנדב לא נכשל בגלל היומן, והכשל נרשם כשגיאה (Error) עם הסבר מה לעשות.
    public class DeliveryTrackerResilienceTests
    {
        private sealed class ListLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception)));
        }

        private static (DeliveryTracker Tracker, ListLogger<DeliveryTracker> Log) Create(Mock<IDeliveryLogRepository> logs, int? routeInProgress = 7)
        {
            var routes = new Mock<IVolunteerRouteRepository>();
            routes.Setup(r => r.GetInProgressRouteIdAsync(It.IsAny<int>())).ReturnsAsync(routeInProgress);
            var log = new ListLogger<DeliveryTracker>();
            return (new DeliveryTracker(logs.Object, routes.Object, new Mock<INotificationService>().Object, TestDb.Mapper, log), log);
        }

        [Fact]
        public async Task ALocationUpdate_DoesNotFail_WhenTheLogTableIsOutOfDate()
        {
            var logs = new Mock<IDeliveryLogRepository>();
            logs.Setup(l => l.GetLastLocationAsync(It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Invalid column name 'Type'."));
            var (tracker, log) = Create(logs);

            var result = await tracker.RecordLocationAsync(volunteerId: 3, 32.08, 34.78);

            Assert.Null(result);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("DeliveryLogTracking"));
        }

        [Fact]
        public async Task AFailedInsert_IsLoggedAsAnError_WithTheLikelyCause()
        {
            var logs = new Mock<IDeliveryLogRepository>();
            logs.Setup(l => l.GetLastLocationAsync(It.IsAny<int>())).ReturnsAsync((DeliveryLog?)null);
            logs.Setup(l => l.TryAddAsync(It.IsAny<DeliveryLog>())).ReturnsAsync(false);
            var (tracker, log) = Create(logs);

            Assert.Null(await tracker.RecordLocationAsync(volunteerId: 3, 32.08, 34.78));
            await tracker.RecordEventAsync(7, DeliveryLogType.PickedUp, "נאסף");

            Assert.Equal(2, log.Entries.FindAll(e => e.Level == LogLevel.Error && e.Message.Contains("dotnet ef database update")).Count);
        }

        [Fact]
        public async Task BeforeTheVolunteerSetsOff_NothingIsWritten()
        {
            var logs = new Mock<IDeliveryLogRepository>(MockBehavior.Strict);
            var (tracker, log) = Create(logs, routeInProgress: null);

            Assert.Null(await tracker.RecordLocationAsync(volunteerId: 3, 32.08, 34.78));
            Assert.Empty(log.Entries);
        }
    }
}
