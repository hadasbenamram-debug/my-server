using AutoMapper;
using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Services
{
    // הכתיבה ליומן המשלוח (טבלת DeliveryLogs) - המקור של המעקב החי:
    //  - בזמן ביצוע מסלול, כל עדכון מיקום של המתנדב נרשם כשורה (Location). מהשורות האלה נבנה מסלול הנסיעה בפועל.
    //  - כל שלב במסלול נרשם כאירוע: אישור, יציאה לדרך, איסוף, מסירה, אישור קבלה, ביטול.
    // כל רישום חדש נשלח מיד (SignalR, DeliveryLogAdded) לקבוצת המסלול - העמותה, הספקים והמנהלים שצופים בו.
    public class DeliveryTracker
    {
        // שורת מיקום אחת לכל 8 שניות לכל היותר, גם אם הלקוח שולח בתדירות גבוהה יותר
        public static readonly TimeSpan MinLocationInterval = TimeSpan.FromSeconds(8);

        // מיקום של המתנדב נחשב עדכני לאירוע (למשל "נאסף") אם התקבל ב-3 הדקות האחרונות
        public static readonly TimeSpan FreshLocationAge = TimeSpan.FromMinutes(3);

        private readonly IDeliveryLogRepository _logs;
        private readonly IVolunteerRouteRepository _routes;
        private readonly INotificationService _notifications;
        private readonly IMapper _mapper;
        private readonly ILogger<DeliveryTracker> _logger;

        public DeliveryTracker(
            IDeliveryLogRepository logs,
            IVolunteerRouteRepository routes,
            INotificationService notifications,
            IMapper mapper,
            ILogger<DeliveryTracker> logger)
        {
            _logs = logs;
            _routes = routes;
            _notifications = notifications;
            _mapper = mapper;
            _logger = logger;
        }

        // עדכון מיקום מהמתנדב. נרשם (ונשלח לצופים) רק כשהמתנדב באמצע מסלול שיצא אליו (InProgress):
        // לפני היציאה לדרך המיקום שלו לא נחשף לאף אחד. null = לא נרשם (אין מסלול בביצוע / מוקדם מדי מהקודם).
        public async Task<DeliveryLogResponseDto?> RecordLocationAsync(int volunteerId, double latitude, double longitude)
        {
            int? routeId = null;
            DeliveryLog? last;
            try
            {
                routeId = await _routes.GetInProgressRouteIdAsync(volunteerId);
                if (routeId == null)
                    return null;

                last = await _logs.GetLastLocationAsync(routeId.Value);
            }
            catch (Exception ex)
            {
                // תיקון 34: בעבר שגיאה כאן (למשל עמודה Type שחסרה במסד) הפילה את עדכון המיקום כולו (500),
                // למרות שהמיקום עצמו כבר נשמר. עכשיו: נרשמת שגיאה ברורה, ועדכון המיקום מצליח.
                _logger.LogError(ex, "Could not read the delivery log of route {RouteId} (volunteer {VolunteerId}). {Hint}",
                    routeId, volunteerId, SchemaHint);
                return null;
            }

            var now = DateTime.UtcNow;
            if (last != null && now - last.Timestamp < MinLocationInterval)
                return null;

            return await TrySaveAsync(new DeliveryLog
            {
                VolunteerRouteId = routeId.Value,
                Type = DeliveryLogType.Location,
                StatusDescription = "עדכון מיקום",
                Latitude = latitude,
                Longitude = longitude,
                Timestamp = now
            });
        }

        // תיקון 34: הסיבה הנפוצה לכשל ביומן - המסד לא עודכן ל-migration האחרון
        private const string SchemaHint =
            "If the DeliveryLogs table is missing the Type / MatchId columns, the DeliveryLogTracking migration was not applied: " +
            "run 'dotnet ef database update --project DAL --startup-project FoodRescue' (see the startup log of DatabaseMigrator).";

        // אירוע במסלול. volunteer - אם יש לו מיקום עדכני, האירוע נרשם במקום שבו קרה (למשל איסוף / מסירה).
        // routeId = null (שידוך בלי מסלול) - אין יומן, לא נרשם כלום.
        public async Task RecordEventAsync(int? routeId, DeliveryLogType type, string description, int? matchId = null, Volunteer? volunteer = null)
        {
            if (routeId == null)
                return;

            var now = DateTime.UtcNow;
            bool fresh = volunteer?.LocationUpdatedAt != null && now - volunteer.LocationUpdatedAt.Value <= FreshLocationAge;

            await TrySaveAsync(new DeliveryLog
            {
                VolunteerRouteId = routeId.Value,
                Type = type,
                MatchId = matchId,
                StatusDescription = description,
                Latitude = fresh ? volunteer!.CurrentLatitude : null,
                Longitude = fresh ? volunteer!.CurrentLongitude : null,
                Timestamp = now
            });
        }

        // שליחה בזמן אמת של רישום שכבר נשמר (למשל רישום ידני מ-api/deliverylogs)
        public async Task<DeliveryLogResponseDto> PublishAsync(DeliveryLog log)
        {
            var dto = _mapper.Map<DeliveryLogResponseDto>(log);
            await _notifications.PublishAsync(RealtimeGroups.Route(log.VolunteerRouteId), RealtimeEvents.DeliveryLogAdded, dto);

            // וגם למנהלים שהמפה החיה של כל המסלולים פתוחה אצלם (ה-DTO כולל את מזהה המסלול)
            await _notifications.PublishAsync(RealtimeGroups.LiveMap, RealtimeEvents.DeliveryLogAdded, dto);
            return dto;
        }

        // היומן לא מכשיל את הפעולה העסקית: אם הרישום נכשל, האיסוף / המסירה כבר נשמרו.
        // תיקון 34: בעבר נרשמה רק אזהרה (Warning) בלי הסבר, ולכן לא היה ברור למה הטבלה ריקה. עכשיו - שגיאה (Error)
        // עם הסיבה הסבירה. שמירה שהצליחה ושליחה בזמן אמת שנכשלה (SignalR) - נפרדות: הרישום כבר במסד.
        private async Task<DeliveryLogResponseDto?> TrySaveAsync(DeliveryLog log)
        {
            try
            {
                if (!await _logs.TryAddAsync(log))
                {
                    _logger.LogError("Could not save a delivery log ({Type}) for route {RouteId}. {Hint}",
                        log.Type, log.VolunteerRouteId, SchemaHint);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not save a delivery log ({Type}) for route {RouteId}. {Hint}",
                    log.Type, log.VolunteerRouteId, SchemaHint);
                return null;
            }

            try
            {
                return await PublishAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delivery log {LogId} was saved, but the live update for route {RouteId} was not sent.",
                    log.Id, log.VolunteerRouteId);
                return _mapper.Map<DeliveryLogResponseDto>(log);
            }
        }
    }
}
