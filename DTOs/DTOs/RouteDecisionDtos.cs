using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // =====================================================================
    // תיקון 75: "למה המנוע החליט כך" (Explainability)
    // צילום של ההחלטה ברגע שהמסלול נוצר: משפך המועמדים, פירוק הציון של כל מתנדב, למה האחרים לא נבחרו,
    // המסלול שנבנה (Knapsack + Held-Karp) והבחירה הגלובלית (Branch & Bound). נשמר כ-JSON במסלול
    // (VolunteerRoute.DecisionJson), כך שההסבר משקף את מה שהמנוע ידע באותו רגע - לא את המצב של היום.
    // =====================================================================

    /// <summary>GET api/matches/decisions/{routeId}</summary>
    public class RouteDecisionDto
    {
        public int Version { get; set; } = 1;

        // ---- מתמלא בקריאה (לא נשמר) ----
        public int RouteId { get; set; }
        public string? RouteStatus { get; set; }
        public string? VolunteerName { get; set; }

        /// <summary>Auto = הריצה השעתית (בחירה גלובלית) · Single = שיבוץ של תרומה אחת (שיבוץ מחדש / הפעלה ידנית) · Manual = המנהל בחר מתנדב</summary>
        public string Mode { get; set; } = "Auto";
        public DateTime DecidedAt { get; set; }
        public int AnchorDonationId { get; set; }
        public double AnchorWeightKg { get; set; }

        /// <summary>ציון הדחיפות של תרומת העוגן ברגע ההחלטה (0-100)</summary>
        public double AnchorUrgency { get; set; }
        public string? Note { get; set; }
        public double MaxSearchRadiusKm { get; set; }

        public DecisionWeightsDto Weights { get; set; } = new();
        public List<DecisionFunnelStepDto> Funnel { get; set; } = new();
        public List<DecisionCandidateDto> Candidates { get; set; } = new();
        public DecisionRouteDto Route { get; set; } = new();

        /// <summary>רק בריצה השעתית: הבחירה הגלובלית בין כל המסלולים של כל התרומות</summary>
        public DecisionGlobalDto? Global { get; set; }
    }

    /// <summary>משקלות רכיבי הציון (מנורמלים, סכומם 1) - כפי שהיו בהגדרות המערכת ברגע ההחלטה</summary>
    public class DecisionWeightsDto
    {
        public double Distance { get; set; }
        public double Urgency { get; set; }
        public double Capacity { get; set; }
        public double Reliability { get; set; }
    }

    /// <summary>שלב במשפך: כמה מתנדבים נשארו אחרי השלב, ואיזה אלגוריתם סינן</summary>
    public class DecisionFunnelStepDto
    {
        public string Label { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>רכיבי הציון של מתנדב (כל אחד 0-1, לפני שקלול) והציון הסופי (0-1)</summary>
    public class DecisionScoreDto
    {
        public double Distance { get; set; }
        public double Urgency { get; set; }
        public double Capacity { get; set; }
        public double Reliability { get; set; }
        public double Total { get; set; }
    }

    public class DecisionCandidateDto
    {
        public int VolunteerId { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>המקום בדירוג (Merge Sort לפי ציון, מרחק ומזהה)</summary>
        public int Rank { get; set; }
        public double DistanceKm { get; set; }
        public DecisionScoreDto Score { get; set; } = new();

        /// <summary>Chosen · LowerValue · TakenByOtherRoute · NoRoute · NotEvaluated</summary>
        public string Outcome { get; set; } = string.Empty;
        public string? Reason { get; set; }

        /// <summary>ערך המסלול שנבנה לו (אם נבנה) - לפיו נבחר המסלול</summary>
        public double? RouteValue { get; set; }
        public double? RouteScore { get; set; }
        public int? Stops { get; set; }
    }

    public class DecisionStopDto
    {
        public int Order { get; set; }
        public int DonationId { get; set; }
        public bool IsAnchor { get; set; }
        public double WeightKg { get; set; }
        public DateTime? EstimatedArrival { get; set; }

        /// <summary>מתמלא בקריאה</summary>
        public string? SupplierName { get; set; }
    }

    public class DecisionRouteDto
    {
        public string? NgoName { get; set; }
        public DateTime Departure { get; set; }
        public DateTime NgoArrival { get; set; }
        public double DistanceKm { get; set; }
        public double DurationMinutes { get; set; }
        public double WeightKg { get; set; }
        public double KnapsackValue { get; set; }

        /// <summary>קבוצת התרומות היא האופטימום של ה-Knapsack</summary>
        public bool KnapsackOptimal { get; set; }

        /// <summary>Matching Score של המסלול (0-100): 60% ציון המתנדב + 40% יעילות המסלול</summary>
        public double MatchScore { get; set; }

        /// <summary>ערך המסלול לבחירה הגלובלית</summary>
        public double Value { get; set; }
        public int NgosConsidered { get; set; }
        public int ExtraDonationsConsidered { get; set; }
        public List<DecisionStopDto> Stops { get; set; } = new();
    }

    public class DecisionGlobalDto
    {
        public int DonationsInRun { get; set; }
        public int CandidateRoutes { get; set; }
        public int RoutesSelected { get; set; }
        public double TotalValue { get; set; }
        public double GreedyValue { get; set; }
        public double UpperBound { get; set; }
        public double GapPercent { get; set; }
        public long Nodes { get; set; }
        public bool ProvedOptimal { get; set; }
    }

    /// <summary>שורה ברשימת ההחלטות האחרונות (GET api/matches/decisions)</summary>
    public class RouteDecisionSummaryDto
    {
        public int RouteId { get; set; }
        public DateTime DecidedAt { get; set; }
        public string Mode { get; set; } = "Auto";
        public string? RouteStatus { get; set; }
        public string? VolunteerName { get; set; }
        public string? NgoName { get; set; }
        public List<int> DonationIds { get; set; } = new();
        public double WeightKg { get; set; }
        public double MatchScore { get; set; }

        /// <summary>כמה מתנדבים נשקלו (השלב הראשון במשפך)</summary>
        public int VolunteersConsidered { get; set; }
        public int CandidatesShown { get; set; }
    }

    /// <summary>GET api/matches/decisions/by-donation/{donationId}</summary>
    public class DecisionLookupDto
    {
        public int RouteId { get; set; }
    }
}
