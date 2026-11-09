using BLL.Interfaces;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Agent
{
    public sealed record SystemOverview(
        int OpenDonations,
        int PendingDonations,
        int AwaitingVolunteerResponse,
        int ActiveAssignments,
        int AvailableVolunteers,
        int NoShowAlerts,
        string EngineState,
        string EngineScheduleCron,
        string LastEngineRun,
        int LastRunMatchedDonations,
        int LastRunRoutesCreated,
        List<string> LastRunProblems);

    public sealed record LiveMission(int RouteId, string Volunteer, string Destination, string Suppliers, string Status, int Stops, string? LastLocationUpdate);

    // כלים של מנהל: תמונת מצב של המערכת ומנוע השידוכים, ומשימות שנמצאות עכשיו בדרך.
    // הכלים נותנים רק לתפקיד מנהל (AgentService), ובנוסף כל כלי בודק שוב (RequireAdmin) - כמו [Authorize(Roles = "Admin")] בקונטרולרים.
    public sealed class AdminTools
    {
        private readonly IAdminReportService _reports;
        private readonly IMatchService _matches;
        private readonly AgentCaller _caller;

        public AdminTools(IAdminReportService reports, IMatchService matches, AgentCaller caller)
        {
            _reports = reports;
            _matches = matches;
            _caller = caller;
        }

        [Description("מחזיר תמונת מצב של המערכת למנהל: תרומות פתוחות וממתינות לשיבוץ, שיבוצים שממתינים לתגובת מתנדב, שיבוצים פעילים, " +
                     "מתנדבים זמינים, התראות No-Show, ומצב מנוע השידוכים (לוח הזמנים, הריצה האחרונה וכמה שובצו בה, " +
                     "ואילו בעיות מנעו שיבוץ בריצה האחרונה).")]
        public async Task<SystemOverview> GetSystemOverviewAsync()
        {
            _caller.RequireAdmin();

            var stats = await _reports.GetDashboardStatsAsync();
            var engine = await _matches.GetEngineStatusAsync();

            return new SystemOverview(
                stats.OpenDonations,
                engine.PendingDonations,
                engine.AwaitingVolunteerResponse,
                engine.ActiveAssignments,
                stats.AvailableVolunteers,
                stats.NoShowAlerts,
                engine.Status == "Running" ? "רץ עכשיו" : "לא רץ כרגע",
                engine.Schedule,
                engine.LastRunAt.HasValue ? AgentFormat.DateAndTime(engine.LastRunAt.Value) : engine.LastRunTime,
                engine.MatchedCount,
                engine.RoutesCreated,
                engine.FailedConstraints.Distinct().Take(10).ToList());
        }

        [Description("מחזיר את המסלולים שמתנדבים נמצאים בהם עכשיו (עד 20): המתנדב, העמותה שאליה הוא נוסע, הספקים, המצב, מספר התחנות " +
                     "ומתי התקבל ממנו מיקום לאחרונה. מספר המסלול משמש לקישור למעקב החי.")]
        public async Task<List<LiveMission>> GetLiveMissionsAsync()
        {
            _caller.RequireAdmin();

            var missions = await _reports.GetLiveMonitorAsync();

            return missions
                .Take(20)
                .Select(m => new LiveMission(
                    m.Id,
                    m.VolunteerName,
                    m.Destination,
                    m.SupplierName,
                    AgentFormat.Match(m.Status),
                    m.StopsCount,
                    AgentFormat.DateAndTime(m.LocationUpdatedAt)))
                .ToList();
        }
    }
}
