using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // תיקון 36: טיפול ידני של המנהל באי-הגעה (No-Show) - עמוד "טיפול באי-הגעה".
    // בעבר: רק כפתור "סמן No-Show" (עם window.prompt) ו"נסה לשבץ שוב" אוטומטי. עכשיו המנהל רואה מי מאחר ובכמה,
    // יכול להתקשר למתנדב, לבחור אם התרומות חוזרות לשיבוץ אוטומטי או שהוא מטפל בעצמו, ולשבץ תרומה למתנדב שהוא בוחר.

    // GET api/matches/no-show/board (מנהל)
    public class NoShowBoardDto
    {
        public DateTime GeneratedAt { get; set; }

        // מסלולים שאושרו ועדיין יש בהם תחנה שלא נאספה - המאחרים ביותר ראשונים
        public List<AwaitingPickupRouteDto> AwaitingPickup { get; set; } = new();

        // אירועי No-Show אחרונים, עם המצב הנוכחי של התרומה
        public List<NoShowEventDto> NoShows { get; set; } = new();
    }

    public class AwaitingPickupRouteDto
    {
        public int? RouteId { get; set; }

        // שידוך אחד מהמסלול, לסימון No-Show (השרת מסמן את כל התחנות שעוד לא נאספו)
        public int MatchId { get; set; }

        public int VolunteerId { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string? VolunteerPhone { get; set; }

        // המתנדב לחץ "יצאתי לדרך"
        public bool Started { get; set; }

        // זמן ההגעה המשוער לתחנה הקרובה שעוד לא נאספה
        public DateTime? NextPickupEta { get; set; }

        // כמה דקות עברו מזמן ההגעה המשוער בלי איסוף (0 = הזמן עוד לא הגיע)
        public int MinutesLate { get; set; }

        // מתי המתנדב שיתף מיקום לאחרונה (null = לא שיתף)
        public DateTime? LastLocationAt { get; set; }

        public List<AwaitingPickupStopDto> Stops { get; set; } = new();
    }

    public class AwaitingPickupStopDto
    {
        public int MatchId { get; set; }
        public int DonationId { get; set; }
        public string? SupplierName { get; set; }
        public string? PickupAddress { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class NoShowEventDto
    {
        public int MatchId { get; set; }
        public int DonationId { get; set; }
        public int? RouteId { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string? VolunteerPhone { get; set; }
        public string? SupplierName { get; set; }
        public string? PickupAddress { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? EstimatedArrival { get; set; }

        // הסטטוס של התרומה עכשיו (Pending = עדיין ממתינה לשיבוץ, Matched = שובצה מחדש...)
        public string DonationStatus { get; set; } = string.Empty;

        // המתנדב שהתרומה שובצה אליו מחדש (אם שובצה)
        public string? ReassignedToVolunteerName { get; set; }

        // אפשר לשבץ אותה עכשיו: ממתינה, מהיום, ושעת האיסוף האחרונה עוד לא עברה
        public bool CanAssign { get; set; }
    }

    // GET api/matches/assign-donation/{donationId}/candidates (מנהל)
    public class ManualAssignmentOptionsDto
    {
        public int DonationId { get; set; }
        public string? SupplierName { get; set; }
        public string? PickupAddress { get; set; }
        public double WeightKg { get; set; }

        // שעת האיסוף האחרונה (UTC)
        public DateTime? PickupDeadline { get; set; }

        // למה אי אפשר לשבץ את התרומה עכשיו (null = אפשר)
        public string? Problem { get; set; }

        // המתנדבים המאושרים והזמינים - המתאימים ביותר ראשונים
        public List<ManualVolunteerOptionDto> Volunteers { get; set; } = new();
    }

    public class ManualVolunteerOptionDto
    {
        public int VolunteerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public double MaxCapacityKg { get; set; }
        public double ReliabilityScore { get; set; }

        // קו אווירי מהמיקום הידוע (GPS עדכני, או מרכז האזור של משמרת הזמינות). null = לא ידוע
        public double? DistanceKm { get; set; }
        public bool HasFreshLocation { get; set; }

        // יש לו משמרת זמינות שמתאימה לתרומה, מעכשיו ועד שעת האיסוף
        public bool AvailableBySchedule { get; set; }

        // כבר דחה / לא ענה בזמן / לא הגיע לתרומה הזו
        public bool FailedThisDonationBefore { get; set; }

        // אפשר לשלוח לו הצעה: הקיבולת מספיקה, ואין לו הצעה פתוחה או מסלול פעיל
        public bool CanAssign { get; set; }

        // הסבר קצר (למשל "יש לו כבר מסלול פעיל")
        public string? Note { get; set; }
    }

    // POST api/matches/assign-donation/{donationId}/volunteer/{volunteerId} (מנהל)
    public class ManualAssignDto
    {
        // המנהל תיאם עם המתנדב (למשל בטלפון) - לא בודקים את מערכת השעות שלו.
        // כל שאר האילוצים נבדקים תמיד: שעת האיסוף, תפוגה, שעות הקבלה בעמותה, קיבולת, שבת.
        public bool IgnoreSchedule { get; set; }
    }
}
