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

        // מנהלים שצופים במפה החיה של כל המסלולים (WatchAllRoutes) - מקבלים את היומן של כל המסלולים.
        // קבוצה נפרדת מ-Admins: עדכוני מיקום נשלחים רק למי שהמפה פתוחה אצלו, לא לכל מנהל מחובר.
        public const string LiveMap = "live-map";
    }

    public static class RealtimeEvents
    {
        public const string Notification = "ReceiveNotification";
        public const string MatchStatusChanged = "MatchStatusChanged";

        // רישום חדש ביומן המשלוח (DeliveryLogResponseDto): עדכון מיקום של המתנדב או שלב במסלול.
        // נשלח לקבוצת המסלול (route:{id}) - כך העמותה רואה את המתנדב זז על המפה בזמן אמת.
        public const string DeliveryLogAdded = "DeliveryLogAdded";
    }

    // כשאין SignalR (בדיקות / ג'וב ברקע בלי Hub) - לא עושה כלום
    public sealed class NullRealtimeNotifier : IRealtimeNotifier
    {
        public Task SendToUserAsync(int userId, string method, object payload) => Task.CompletedTask;
        public Task SendToGroupAsync(string group, string method, object payload) => Task.CompletedTask;
    }
}
