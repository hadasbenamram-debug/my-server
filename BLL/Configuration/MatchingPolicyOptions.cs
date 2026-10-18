namespace BLL.Configuration
{
    // מדיניות מנוע השידוכים. ערכי ברירת המחדל נקראים מ-appsettings (MatchingPolicy),
    // והמנהל יכול לדרוס חלק מהם דרך api/settings (נשמר בטבלת SystemSettings).
    // כל השירותים מקבלים את הערכים העדכניים דרך IMatchingPolicyProvider.
    public class MatchingPolicyOptions
    {
        public const string SectionName = "MatchingPolicy";

        /// <summary>מתי רץ מנוע השידוכים (Cron). ברירת מחדל: פעם בשעה, כמו במסמך התיאור.</summary>
        public string EngineCron { get; set; } = "0 * * * *";

        /// <summary>מספר מקסימלי של תרומות מאותה קטגוריה בנסיעה אחת. 0 או פחות = ללא הגבלה. ברירת מחדל: 3.</summary>
        public int MaxItemsPerCategory { get; set; } = 3;

        /// <summary>גודל יחידת המשקל בגרמים לחישוב ה-Knapsack (ברירת מחדל: 100 גרם).</summary>
        public int UnitSizeGrams { get; set; } = 100;

        public double AverageSpeedKmH { get; set; } = 40.0;

        /// <summary>מקדם עקיפה: דרך בפועל / קו אווירי. משמש רק להערכות שאינן מגיעות מ-Google Maps.</summary>
        public double RoadDetourFactor { get; set; } = 1.3;

        /// <summary>אזור הזמן של "שעות הקיר" (שעות קבלה, מערכת שעות, LatestPickupTime). "UTC" = ההתנהגות הישנה.</summary>
        public string TimeZoneId { get; set; } = "Asia/Jerusalem";

        public double MaxSearchRadiusKm { get; set; } = 20.0;

        /// <summary>תרומות נוספות למסלול נבחרות רק אם הן במרחק כזה (קו אווירי) מתרומת העוגן.</summary>
        public double ClusterRadiusKm { get; set; } = 10.0;

        /// <summary>כמה מתנדבים מובילים בונים להם מסלול מלא לכל תרומה, כדי לבחור את המסלול עם ה-Score הגבוה.</summary>
        public int MaxRoutesEvaluatedPerDonation { get; set; } = 5;

        /// <summary>חלון האישור של המתנדב (הדוגמה במסמך התיאור: 5 דקות).</summary>
        public int ResponseTimeoutMinutes { get; set; } = 5;

        /// <summary>זמן טעינה/פריקה בכל תחנה (דקות) — נכלל בחישוב ה-ETA.</summary>
        public double StopServiceMinutes { get; set; } = 5.0;

        /// <summary>מיקום GPS ישן מזה (בדקות) נחשב לא אמין.</summary>
        public int MaxLocationAgeMinutes { get; set; } = 30;

        /// <summary>0 = כבוי. אם &gt;0: שיבוץ מאושר שהמתנדב לא סימן איסוף עד ETA + X דקות מסומן No-Show אוטומטית.</summary>
        public int AutoNoShowGraceMinutes { get; set; } = 0;

        /// <summary>הפחתה בציון האמינות (סקאלה 1-5) על כל מסלול שהסתיים ב-No-Show.</summary>
        public double NoShowReliabilityPenalty { get; set; } = 0.5;

        /// <summary>קבוע הזמן (בדקות) של נוסחת הדחיפות: 100·e^(-דקות עד המועד האחרון / קבוע).</summary>
        public double UrgencyTimeConstantMinutes { get; set; } = 180.0;

        /// <summary>מקדם הענישה על מרחק בנוסחת ה-Value של ה-Knapsack (ראו KnapsackItem.CreateItems).</summary>
        public double DistancePenaltyCoefficient { get; set; } = 0.1;

        /// <summary>כמה זמן (דקות) נשמר במטמון מרחק/זמן נסיעה שהתקבל מ-Google. הערכה (Haversine) נשמרת 5 דקות בלבד.</summary>
        public int DistanceCacheMinutes { get; set; } = 180;

        // ===== משקלות ציון המתנדב (VolunteerMatchScorer). המנהל יכול לשנות דרך api/settings. =====
        public double DistanceWeight { get; set; } = 0.35;
        public double UrgencyWeight { get; set; } = 0.30;
        public double CapacityWeight { get; set; } = 0.15;
        public double ReliabilityWeight { get; set; } = 0.20;

        public MatchingPolicyOptions Clone() => (MatchingPolicyOptions)MemberwiseClone();
    }
}
