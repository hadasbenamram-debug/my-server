using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // נקודה במסלול הנסיעה בפועל (מתוך יומן המשלוח)
    public class TrackingPointDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime At { get; set; }
    }

    // מעקב חי אחרי מסלול (GET api/tracking/routes/{id}) - לעמותה המקבלת, לספקים שבמסלול, למתנדב ולמנהל.
    // הכול נבנה מטבלת DeliveryLogs: מסלול הנסיעה בפועל, המיקום האחרון והאירועים.
    // אחרי הטעינה, כל רישום חדש ביומן מגיע בזמן אמת דרך SignalR (WatchRoute + האירוע DeliveryLogAdded).
    public class RouteTrackingDto
    {
        // התכנון: התחנות (עם הסטטוס של כל אחת), העמותה, וקו המסלול המתוכנן (Polyline מקודד)
        public RouteDto Route { get; set; } = new();

        // המתנדב בדרך עכשיו (המסלול InProgress) - יש מיקום חי
        public bool IsLive { get; set; }

        // המיקום האחרון של המתנדב (רק בזמן ביצוע המסלול)
        public TrackingPointDto? CurrentPosition { get; set; }

        // מסלול הנסיעה בפועל, מהיציאה לדרך ועד עכשיו (המיקום לא נחשף לפני היציאה לדרך)
        public List<TrackingPointDto> Trail { get; set; } = new();

        // האירועים במסלול לפי הסדר (בלי עדכוני המיקום)
        public List<DeliveryLogResponseDto> Events { get; set; } = new();

        // ההתקדמות
        public int TotalStops { get; set; }
        public int PickedUpStops { get; set; }
        public int DeliveredStops { get; set; }

        // זמן ההגעה המשוער לעמותה, מהמיקום הנוכחי דרך התחנות שנשארו
        public DateTime? EstimatedNgoArrival { get; set; }
        public double? RemainingMinutes { get; set; }
        public double? RemainingDistanceKm { get; set; }

        // קו הדרך שנשארה (מהמיקום הנוכחי) מ-Google Routes, Polyline מקודד. null = אין (ואז מציירים קו ישר)
        public string? RemainingPolyline { get; set; }

        // מקור ההערכה: live (Google, לפי התנועה עכשיו), estimate (קו אווירי), plan (התכנון המקורי), none
        public string EtaSource { get; set; } = "none";

        public DateTime GeneratedAt { get; set; }
    }
}
