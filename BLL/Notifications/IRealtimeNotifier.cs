using System.Threading.Tasks;

namespace BLL.Notifications
{
    // דחיפה בזמן אמת ללקוח (SignalR). המימוש נמצא בשכבת ה-API (NotificationHub), כאן רק החוזה.
    public interface IRealtimeNotifier
    {
        Task SendToUserAsync(int userId, string method, object payload);
        Task SendToGroupAsync(string group, string method, object payload);
    }

    public static class RealtimeGroups
    {
        public const string Admins = "admins";
        public static string User(int userId) => $"user:{userId}";
        public static string Route(int routeId) => $"route:{routeId}";
    }

    public static class RealtimeEvents
    {
        public const string Notification = "ReceiveNotification";
        public const string MatchStatusChanged = "MatchStatusChanged";
        public const string VolunteerLocation = "VolunteerLocation";
    }

    // כשאין SignalR (בדיקות / ג'וב ברקע בלי Hub) - לא עושה כלום
    public sealed class NullRealtimeNotifier : IRealtimeNotifier
    {
        public Task SendToUserAsync(int userId, string method, object payload) => Task.CompletedTask;
        public Task SendToGroupAsync(string group, string method, object payload) => Task.CompletedTask;
    }
}
