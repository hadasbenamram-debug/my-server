using BLL.Exceptions;
using BLL.Notifications;
using BLL.Services;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // המפה החיה של המנהל: כל המסלולים שבדרך עכשיו על מפה אחת, וכל רישום ביומן של כל מסלול מגיע אליה בזמן אמת
    public sealed class LiveRoutesMapTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private static CurrentUserDto As(int userId, bool admin = false) => new() { UserId = userId, IsAdmin = admin };

        private CurrentUserDto Admin => As(_h.AdminUser.Id, admin: true);

        private DeliveryTrackingService Tracking() => new(
            _h.MatchService, _h.DeliveryLogs, new EstimateOnlyDistances(_h.Policy), _h.Policy,
            new MemoryCache(new MemoryCacheOptions()), TestDb.Mapper);

        // מתנדב, תרומה, ריצת מנוע ואישור המסלול (עדיין Planned - לא יצא לדרך)
        private async Task<(Volunteer Volunteer, int RouteId)> AcceptedRouteAsync()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("driver@test");
            await _h.AddDonationAsync();
            await _h.Engine.RunBatchAsync("Manual");
            _h.Fresh();

            var matchId = (await _h.Db.Matches.SingleAsync()).Id;
            var route = (await _h.MatchService.AcceptAsync(matchId, As(volunteer.UserId))).Route!;
            _h.Fresh();
            return (volunteer, route.Id);
        }

        private async Task StartAsync(Volunteer volunteer, int routeId)
        {
            await _h.MatchService.StartRouteAsync(routeId, As(volunteer.UserId));
            _h.Fresh();
        }

        // כדי לא לחכות 8 שניות בין עדכוני מיקום
        private async Task AgeLogsAsync(TimeSpan by)
        {
            foreach (var log in await _h.Db.DeliveryLogs.ToListAsync())
                log.Timestamp -= by;
            foreach (var route in await _h.Db.VolunteerRoutes.Where(r => r.StartedAt != null).ToListAsync())
                route.StartedAt -= by;
            await _h.Db.SaveChangesAsync();
            _h.Fresh();
        }

        [Fact]
        public async Task LiveRoutes_ShowOnlyRoutesOnTheRoad_WithTheTrailAndTheVolunteersPosition()
        {
            var (volunteer, routeId) = await AcceptedRouteAsync();

            // אושר אבל עוד לא יצא לדרך - לא על המפה
            Assert.Empty(await Tracking().GetLiveRoutesAsync(Admin));

            await StartAsync(volunteer, routeId);
            await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0810, 34.7810);
            await AgeLogsAsync(TimeSpan.FromSeconds(10));
            await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0820, 34.7820);
            _h.Fresh();

            var live = Assert.Single(await Tracking().GetLiveRoutesAsync(Admin));

            Assert.Equal(routeId, live.Route.Id);
            Assert.True(live.IsLive);
            Assert.Equal(2, live.Trail.Count);
            Assert.Equal(32.0820, live.CurrentPosition!.Latitude, 6);
            Assert.Equal(1, live.TotalStops);
            Assert.False(string.IsNullOrEmpty(live.Route.VolunteerName));
            Assert.Equal("plan", live.EtaSource);          // בלי פנייה ל-Google לכל מסלול
            Assert.True(live.Events.Count <= 3);
            Assert.Equal("RouteStarted", live.Events.Last().Type);
        }

        [Fact]
        public async Task LiveRoutes_AreOnlyForAdmins()
        {
            var (volunteer, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);

            // גם העמותה של המסלול וגם המתנדב עצמו - רק המסלול שלהם (api/tracking/routes/{id}), לא כל המסלולים
            await Assert.ThrowsAsync<ForbiddenException>(() => Tracking().GetLiveRoutesAsync(As(_h.Ngo.UserId)));
            await Assert.ThrowsAsync<ForbiddenException>(() => Tracking().GetLiveRoutesAsync(As(volunteer.UserId)));
        }

        [Fact]
        public async Task EveryLogEntry_IsAlsoSentToTheLiveMapGroup_WithItsRouteId()
        {
            var (volunteer, routeId) = await AcceptedRouteAsync();
            await StartAsync(volunteer, routeId);
            await _h.Tracker.RecordLocationAsync(volunteer.Id, 32.0810, 34.7810);

            List<DeliveryLogResponseDto> toLiveMap = _h.Realtime.Sent
                .Where(s => s.Target == RealtimeGroups.LiveMap && s.Method == RealtimeEvents.DeliveryLogAdded)
                .Select(s => (DeliveryLogResponseDto)s.Payload)
                .ToList();

            Assert.Equal(new[] { "Accepted", "RouteStarted", "Location" }, toLiveMap.Select(l => l.Type));
            Assert.All(toLiveMap, l => Assert.Equal(routeId, l.VolunteerRouteId));
        }
    }
}
