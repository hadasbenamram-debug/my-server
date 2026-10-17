using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // סטטוס מנוע השידוכים (GET api/Matches/engine-status) - נתונים אמיתיים מהריצה האחרונה ומהמסד
    public class EngineStatusDto
    {
        public string Status { get; set; } = "Idle";      // Idle / Running
        public DateTime? LastRunAt { get; set; }
        public string LastRunTime { get; set; } = "טרם בוצעה ריצה";
        public string? LastRunTrigger { get; set; }
        public int MatchedCount { get; set; }            // תרומות ששובצו בריצה האחרונה
        public int RoutesCreated { get; set; }
        public int DonationsProcessed { get; set; }
        public double? LastRunDurationSeconds { get; set; }
        public int PendingDonations { get; set; }
        public int AwaitingVolunteerResponse { get; set; }
        public int ActiveAssignments { get; set; }
        public string Schedule { get; set; } = string.Empty;
        public List<string> FailedConstraints { get; set; } = new();
    }

    // תוצאת ריצה ידנית (POST api/Matches/run-engine)
    public class EngineRunResultDto
    {
        public bool Started { get; set; }
        public string Message { get; set; } = string.Empty;
        public int DonationsProcessed { get; set; }
        public int MatchedCount { get; set; }
        public int RoutesCreated { get; set; }
        public double DurationSeconds { get; set; }
        public List<string> FailedConstraints { get; set; } = new();
    }
}
