using BLL.Notifications;
using BLL.Services;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace FoodRescue.Tests
{
    // המעקב החי של העמותה: יומן המשלוח (DeliveryLogs) נכתב בכל שלב ובכל עדכון מיקום, נשלח בזמן אמת
    // לקבוצת המסלול, ומסך המעקב נבנה ממנו. ובנוסף: מפתח הדפדפן של Google Maps לא יכול להיות מפתח השרת.
    public sealed class DeliveryTrackingTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private static CurrentUserDto As(int userId, bool admin = false) => new() { UserId = userId, IsAdmin = admin };

        private DeliveryTrackingService Tracking() => new(
            _h.MatchService, _h.DeliveryLogs, new EstimateOnlyDistances(_h.Policy), _h.Policy,
            new MemoryCache(new MemoryCacheOptions()), TestDb.Mapper, _h.Clock);

        // מתנדב, תרומה, ריצת מנוע ואישור המסלול. מחזיר את המתנדב, השידוך והמסלול (עדיין Planned).
        private async Task<(Volunteer Volunteer, int MatchId, int RouteId)> AcceptedRouteAsync()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("driver@test");
            await _h.AddDonationAsync();
            await _h.Engine.RunBatchAsync("Manual");
            _h.Fresh();

            var matchId = (await _h.Db.Matches.SingleAsync()).Id;
            var route = (await _h.MatchService.AcceptAsync(matchId, As(volunteer.UserId))).Route!;
            _h.Fresh();
            return (volunteer, matchId, route.Id);
        }

        private async Task StartAsync(Volunteer volunteer, int routeId)
        {
            await _h.MatchService.StartRouteAsync(routeId, As(volunteer.UserId));
            _h.Fresh();
        }

        // כדי לא לחכות 8 שניות בין עדכוני מיקום - מזיזים אחורה את מה שכבר קרה (הרישומים ושעת היציאה לדרך)
        private async Task AgeLogsAsync(TimeSpan by)
        {
            foreach (var log in await _h.Db.DeliveryLogs.ToListAsync())
                log.Timestamp -= by;
            foreach (var route in await _h.Db.VolunteerRoutes.Where(r => r.StartedAt != null).ToListAsync())
                route.StartedAt -= by;
            await _h.Db.SaveChangesAsync();
            _h.Fresh();
        }

        private List<DeliveryLogResponseDto> SentToRoute(int routeId) =>
            _h.Realtime.Sent
                .Where(s => s.Target == RealtimeGroups.Route(routeId) && s.Method == RealtimeEvents.DeliveryLogAdded)
                .Select(s => (DeliveryLogResponseDto)s.Payload)
                .ToList();

        // ---------------- כתיבה ליומן ----------------

        [Fact]
        public async Task Location_IsNotRecordedOrShared_BeforeTheVolunteerStarts()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();

            Assert.Null(await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.07, 34.77));

            Assert.False(await _h.Db.DeliveryLogs.AnyAsync(l => l.Type == DeliveryLogType.Location));
            Assert.DoesNotContain(SentToRoute(routeId), l => l.Type == nameof(DeliveryLogType.Location));
        }

        [Fact]
        public async Task Location_WhileTheRouteIsInProgress_IsWrittenToTheLog_AndSentLive_AtMostEvery8Seconds()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);

            var first = await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0701, 34.7701);
            Assert.NotNull(first);
            Assert.Equal(nameof(DeliveryLogType.Location), first!.Type);

            // מיד אחרי - לא נרשם שוב
            Assert.Null(await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0702, 34.7702));

            await AgeLogsAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0703, 34.7703));

            _h.Fresh();
            var locations = await _h.Db.DeliveryLogs.Where(l => l.Type == DeliveryLogType.Location).OrderBy(l => l.Id).ToListAsync();
            Assert.Equal(2, locations.Count);
            Assert.All(locations, l => Assert.Equal(routeId, l.VolunteerRouteId));
            Assert.Equal(32.0703, locations[1].Latitude!.Value, 6);

            var sent = SentToRoute(routeId).Where(l => l.Type == nameof(DeliveryLogType.Location)).ToList();
            Assert.Equal(2, sent.Count);
        }

        [Fact]
        public async Task EveryStepOfTheRoute_IsWrittenToTheLog_AndSentToTheRouteGroup()
        {
            var (volunteer, matchId, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);
            await _h.MatchService.PickUpAsync(matchId, As(volunteer.UserId));
            _h.Fresh();
            await _h.MatchService.CompleteAsync(matchId, As(volunteer.UserId));
            _h.Fresh();
            var donationId = (await _h.Db.Matches.SingleAsync()).DonationId;
            await _h.Workflow.ConfirmReceiptAsync(donationId, _h.Ngo.Id);
            _h.Fresh();

            var logs = await _h.Db.DeliveryLogs.OrderBy(l => l.Id).ToListAsync();
            Assert.Equal(
                new[] { DeliveryLogType.Accepted, DeliveryLogType.RouteStarted, DeliveryLogType.PickedUp, DeliveryLogType.Delivered, DeliveryLogType.ReceiptConfirmed },
                logs.Select(l => l.Type));
            Assert.All(logs, l => Assert.Equal(routeId, l.VolunteerRouteId));

            // האיסוף והמסירה שייכים לתחנה
            Assert.Equal(matchId, logs.Single(l => l.Type == DeliveryLogType.PickedUp).MatchId);
            Assert.Equal(matchId, logs.Single(l => l.Type == DeliveryLogType.Delivered).MatchId);
            Assert.Contains(_h.Supplier.BusinessName, logs.Single(l => l.Type == DeliveryLogType.PickedUp).StatusDescription);

            // היציאה לדרך נרשמת בלי מיקום (לא חושפים מאיפה המתנדב יצא)
            var started = logs.Single(l => l.Type == DeliveryLogType.RouteStarted);
            Assert.Null(started.Latitude);
            Assert.Null(started.Longitude);

            Assert.Equal(5, SentToRoute(routeId).Count);
        }

        [Fact]
        public async Task TheNgosNotification_WhenTheVolunteerSetsOff_LinksToTheLiveTrackingScreen()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);

            var ngoNotification = await _h.Db.Notifications
                .Where(n => n.UserId == _h.Ngo.UserId && n.Subject == "המתנדב יצא לדרך")
                .SingleAsync();
            Assert.Equal($"/app/track/{routeId}", ngoNotification.Link);
        }

        [Fact]
        public async Task TheNotifications_WhenTheDonationIsDelivered_LinkToTheDonationScreen()
        {
            // תיקון 63: הדף "אישור קבלה" נמחק - העמותה מאשרת קבלה במסך התרומה, וההתראה מובילה לשם
            var (volunteer, matchId, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);
            await _h.MatchService.PickUpAsync(matchId, As(volunteer.UserId));
            _h.Fresh();
            await _h.MatchService.CompleteAsync(matchId, As(volunteer.UserId));
            _h.Fresh();

            var donationId = (await _h.Db.Matches.SingleAsync()).DonationId;
            var ngoNotification = await _h.Db.Notifications
                .Where(n => n.UserId == _h.Ngo.UserId && n.Subject == $"תרומה #{donationId} נמסרה")
                .SingleAsync();
            Assert.Equal($"/app/ngo/donation/{donationId}", ngoNotification.Link);

            var supplierNotification = await _h.Db.Notifications
                .Where(n => n.UserId == _h.Supplier.UserId && n.Subject == $"תרומה #{donationId} נמסרה לעמותה")
                .SingleAsync();
            Assert.Equal($"/app/supplier/donation/{donationId}", supplierNotification.Link);
        }

        [Fact]
        public async Task NoShow_IsWrittenToTheLog()
        {
            var (_, matchId, routeId) = await AcceptedRouteAsync();

            await _h.MatchService.MarkNoShowAsync(matchId, "לא הגיע");
            _h.Fresh();

            var cancelled = await _h.Db.DeliveryLogs.SingleAsync(l => l.Type == DeliveryLogType.Cancelled);
            Assert.Equal(routeId, cancelled.VolunteerRouteId);
        }

        // ---------------- מסך המעקב ----------------

        [Fact]
        public async Task Tracking_ForTheReceivingNgo_ShowsTheTrail_TheEvents_AndAnEta()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);

            await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0810, 34.7810);
            await AgeLogsAsync(TimeSpan.FromSeconds(10));
            await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0820, 34.7820);
            _h.Fresh();

            var tracking = await Tracking().GetRouteTrackingAsync(routeId, As(_h.Ngo.UserId));

            Assert.NotNull(tracking);
            Assert.True(tracking!.IsLive);
            Assert.Equal(2, tracking.Trail.Count);
            Assert.Equal(32.0820, tracking.CurrentPosition!.Latitude, 6);
            Assert.Equal(new[] { "Accepted", "RouteStarted" }, tracking.Events.Select(e => e.Type));
            Assert.Equal(1, tracking.TotalStops);
            Assert.Equal(0, tracking.PickedUpStops);

            // בלי Google: הערכה לפי קו אווירי מהמיקום הנוכחי, דרך התחנה, עד העמותה
            Assert.Equal("estimate", tracking.EtaSource);
            Assert.NotNull(tracking.EstimatedNgoArrival);
            Assert.True(tracking.RemainingDistanceKm > 0);
            Assert.NotNull(tracking.Route.StartedAt);
        }

        [Fact]
        public async Task Tracking_IsOnlyForTheRoutesParticipants()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();

            var otherNgoUser = EngineHarness.NewUser("other-ngo@test", UserRole.Ngo);
            var otherSupplierUser = EngineHarness.NewUser("other-supplier@test", UserRole.Supplier);
            _h.Db.Users.AddRange(otherNgoUser, otherSupplierUser);
            await _h.Db.SaveChangesAsync();
            _h.Db.Ngos.Add(_h.NewNgo(otherNgoUser.Id, ApprovalStatus.Approved, "עמותה אחרת"));
            _h.Db.Suppliers.Add(new Supplier
            {
                UserId = otherSupplierUser.Id, BusinessName = "ספק אחר", Address = "רחוב 2", Latitude = 32.1, Longitude = 34.8,
                TaxId = "987654321", GooglePlaceId = "N/A", BusinessLicenseFilePath = "y.pdf", ApprovalStatus = ApprovalStatus.Approved
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            Assert.Null(await Tracking().GetRouteTrackingAsync(routeId, As(otherNgoUser.Id)));
            Assert.Null(await Tracking().GetRouteTrackingAsync(routeId, As(otherSupplierUser.Id)));
            Assert.Null(await Tracking().GetRouteTrackingAsync(routeId + 1000, As(_h.Ngo.UserId)));

            Assert.NotNull(await Tracking().GetRouteTrackingAsync(routeId, As(_h.Ngo.UserId)));
            Assert.NotNull(await Tracking().GetRouteTrackingAsync(routeId, As(_h.Supplier.UserId)));
            Assert.NotNull(await Tracking().GetRouteTrackingAsync(routeId, As(volunteer.UserId)));
            Assert.NotNull(await Tracking().GetRouteTrackingAsync(routeId, As(_h.AdminUser.Id, admin: true)));
        }

        [Fact]
        public async Task Tracking_BeforeTheVolunteerSetsOff_HasNoPosition_AndUsesThePlannedEta()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();

            // רישום ידני עם מיקום מלפני היציאה לדרך (למשל מהבית) - לא נחשף
            _h.Db.DeliveryLogs.Add(new DeliveryLog
            {
                VolunteerRouteId = routeId, Type = DeliveryLogType.Note, StatusDescription = "בדיקה",
                Latitude = 31.5, Longitude = 34.5, Timestamp = _h.NowUtc.AddMinutes(-5)
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var tracking = (await Tracking().GetRouteTrackingAsync(routeId, As(_h.Ngo.UserId)))!;

            Assert.False(tracking.IsLive);
            Assert.Null(tracking.CurrentPosition);
            Assert.Empty(tracking.Trail);
            Assert.Equal("plan", tracking.EtaSource);
            Assert.Null(tracking.Route.VolunteerLatitude);

            // גם אחרי היציאה לדרך - הרישום מלפני היציאה לא נכנס למסלול הנסיעה
            await StartAsync(volunteer, routeId);
            tracking = (await Tracking().GetRouteTrackingAsync(routeId, As(_h.Ngo.UserId)))!;
            Assert.True(tracking.IsLive);
            Assert.Empty(tracking.Trail);
        }

        [Fact]
        public async Task RouteDetails_ShowTheVolunteersLocation_OnlyWhileTheRouteIsInProgress()
        {
            var (volunteer, matchId, routeId) = await AcceptedRouteAsync();

            // למתנדב יש מיקום (מהרתמה), אבל הוא עוד לא יצא לדרך
            Assert.Null((await _h.MatchService.GetRouteAsync(routeId, As(_h.Ngo.UserId)))!.VolunteerLatitude);

            await StartAsync(volunteer, routeId);
            Assert.NotNull((await _h.MatchService.GetRouteAsync(routeId, As(_h.Ngo.UserId)))!.VolunteerLatitude);

            await _h.MatchService.CompleteAsync(matchId, As(volunteer.UserId));
            _h.Fresh();
            Assert.Null((await _h.MatchService.GetRouteAsync(routeId, As(_h.Ngo.UserId)))!.VolunteerLatitude);
        }

        [Fact]
        public async Task ManualLog_IsSentLiveToTheRouteGroup()
        {
            var (volunteer, _, routeId) = await AcceptedRouteAsync();
            var service = new DeliveryLogService(_h.DeliveryLogs, _h.Routes, _h.Volunteers, _h.Tracker, _h.Caller, TestDb.Mapper);

            var created = await service.CreateAsync(
                new CreateDeliveryLogDto { VolunteerRouteId = routeId, Status = "עומד בפקק", Notes = "" }, As(volunteer.UserId));

            Assert.Equal(nameof(DeliveryLogType.Note), created.Type);
            Assert.Contains(SentToRoute(routeId), l => l.Id == created.Id);
        }

        // ---------------- ניקוי ----------------

        [Fact]
        public async Task Cleanup_DeletesOldLocations_ButKeepsTheRouteEvents()
        {
            var (_, _, routeId) = await AcceptedRouteAsync();
            var old = _h.NowUtc.AddDays(-(MaintenanceJobs.LocationRetentionDays + 5));
            _h.Db.DeliveryLogs.AddRange(
                new DeliveryLog { VolunteerRouteId = routeId, Type = DeliveryLogType.Location, StatusDescription = "עדכון מיקום", Latitude = 32, Longitude = 34.8, Timestamp = old },
                new DeliveryLog { VolunteerRouteId = routeId, Type = DeliveryLogType.PickedUp, StatusDescription = "נאסף", Timestamp = old },
                new DeliveryLog { VolunteerRouteId = routeId, Type = DeliveryLogType.Location, StatusDescription = "עדכון מיקום", Latitude = 32, Longitude = 34.8, Timestamp = _h.NowUtc });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var jobs = new MaintenanceJobs(_h.Donations, _h.ActionTokenRepo, new LoginAttemptRepository(_h.Db), _h.Workflow, _h.UnitOfWork,
                TestDb.Log<MaintenanceJobs>(), _h.DeliveryLogs, _h.Clock);
            await jobs.CleanupAsync();
            _h.Fresh();

            var left = await _h.Db.DeliveryLogs.Where(l => l.Type != DeliveryLogType.Accepted).ToListAsync();
            Assert.Equal(2, left.Count);
            Assert.Contains(left, l => l.Type == DeliveryLogType.PickedUp);
            Assert.Contains(left, l => l.Type == DeliveryLogType.Location && l.Timestamp > old);
        }

        // ---------------- מפתח הדפדפן של Google Maps ----------------

        private static MapService MapsWith(Dictionary<string, string?> settings) =>
            new(null!, null!, null!, null!, null!, TestDb.Mapper, TestDb.Config(settings), TestDb.Log<MapService>());

        [Fact]
        public void BrowserConfig_WithoutABrowserKey_IsDisabled()
        {
            var config = MapsWith(new() { ["GoogleMaps:ApiKey"] = "server-key" }).GetBrowserConfig();

            Assert.False(config.Enabled);
            Assert.Null(config.ApiKey);
            Assert.Equal("DEMO_MAP_ID", config.MapId);
        }

        [Fact]
        public void BrowserConfig_NeverSendsTheServerKey_EvenIfItWasSetAsTheBrowserKey()
        {
            var config = MapsWith(new() { ["GoogleMaps:ApiKey"] = "server-key", ["GoogleMaps:BrowserApiKey"] = " server-key " }).GetBrowserConfig();

            Assert.False(config.Enabled);
            Assert.Null(config.ApiKey);
        }

        [Fact]
        public void BrowserConfig_WithASeparateKey_ReturnsOnlyTheBrowserKey()
        {
            var config = MapsWith(new()
            {
                ["GoogleMaps:ApiKey"] = "server-key",
                ["GoogleMaps:BrowserApiKey"] = "browser-key",
                ["GoogleMaps:MapId"] = "my-map-id"
            }).GetBrowserConfig();

            Assert.True(config.Enabled);
            Assert.Equal("browser-key", config.ApiKey);
            Assert.Equal("my-map-id", config.MapId);
            Assert.Equal("he", config.Language);
        }
    }
}
