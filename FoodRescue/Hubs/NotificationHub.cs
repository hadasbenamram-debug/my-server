using BLL.Interfaces;
using BLL.Notifications;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace FoodRescue.Hubs
{
    // ערוץ הזמן-אמת של המערכת (בעבר המחלקה הייתה ריקה ולא נשלח דרכה כלום).
    // קבוצות: user:{id} - התראות אישיות; admins - כל המנהלים; route:{id} - מי שצופה במסלול (מיקום מתנדב / סטטוס).
    // אירועים שהשרת שולח: ReceiveNotification, MatchStatusChanged, VolunteerLocation.
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly IVolunteerService _volunteerService;
        private readonly IMatchService _matchService;

        public NotificationHub(IVolunteerService volunteerService, IMatchService matchService)
        {
            _volunteerService = volunteerService;
            _matchService = matchService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.GetUserId();
            if (userId == null)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId.Value));

            if (Context.User!.IsInRole("Admin"))
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Admins);

            await base.OnConnectedAsync();
        }

        // מתנדב משתף מיקום חי (נשמר ונשלח לצופים במסלול הפעיל שלו)
        public async Task UpdateLocation(double latitude, double longitude)
        {
            var userId = Context.User?.GetUserId() ?? throw new HubException("לא מחובר.");

            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                throw new HubException("מיקום לא תקין.");

            await _volunteerService.UpdateMyLocationAsync(userId, latitude, longitude);
        }

        // הצטרפות למעקב אחרי מסלול - רק למי שמורשה לראות אותו (מנהל, המתנדב, העמותה או ספק בתוך המסלול)
        public async Task WatchRoute(int routeId)
        {
            var route = await _matchService.GetRouteAsync(routeId, Context.User!.ToCurrentUser());
            if (route == null)
                throw new HubException("המסלול לא נמצא.");

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Route(routeId));
        }

        public Task UnwatchRoute(int routeId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Route(routeId));
    }

    // המימוש של IRealtimeNotifier (מוגדר ב-BLL) מעל SignalR
    public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
    {
        private readonly IHubContext<NotificationHub> _hub;

        public SignalRRealtimeNotifier(IHubContext<NotificationHub> hub)
        {
            _hub = hub;
        }

        public Task SendToUserAsync(int userId, string method, object payload) =>
            _hub.Clients.Group(RealtimeGroups.User(userId)).SendAsync(method, payload);

        public Task SendToGroupAsync(string group, string method, object payload) =>
            _hub.Clients.Group(group).SendAsync(method, payload);
    }
}
