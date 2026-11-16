using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IMatchRepository : IRepository<Match>
    {
        // כל השידוכים הפעילים (ממתינים / אושרו / באיסוף / בדרך), כולל פרטי תרומה, עמותה ומתנדב
        Task<List<Match>> GetActiveMatchesAsync();

        // השידוכים הפעילים שהמשתמש משתתף בהם - מסונן במסד. מזהה -1 = אין פרופיל כזה.
        Task<List<Match>> GetActiveForParticipantAsync(int volunteerId, int ngoId, int supplierId);

        Task<PagedResult<Match>> GetPageAsync(int skip, int take, MatchStatus? status);

        // האם למתנדב יש שיבוץ פעיל (Confirmed / InPickup / InTransit) שאינו השידוך הנוכחי.
        // matches של אותו מסלול (VolunteerRouteId זהה) נחשבים כשיבוץ אחד ולכן אינם נספרים.
        Task<bool> HasActiveConfirmedMatchAsync(int volunteerId, int excludeMatchId, int? excludeRouteId = null);

        // שידוך + התרומה שלו, לקריאה בלבד
        Task<Match?> GetByIdWithDonationAsync(int id);

        // שידוך במעקב כולל התרומה, המתנדב, העמותה והמסלול - לשינוי סטטוס
        Task<Match?> GetByIdWithDetailsAsync(int id);

        // שידוך עם המזהה הזה *וגם* של המתנדב הזה (null = לא קיים או לא שלו)
        Task<Match?> GetOwnedByVolunteerAsync(int id, int volunteerId);

        // כל השידוכים של מסלול (במעקב, כולל תרומה/מתנדב/עמותה)
        Task<List<Match>> GetByRouteIdAsync(int routeId);

        // השידוכים הפעילים במסלול, רק של המתנדב הזה
        Task<List<Match>> GetActiveByRouteForVolunteerAsync(int routeId, int volunteerId);

        // האם קיים שידוך על התרומה שהמתנדב או העמותה משתתפים בו
        Task<bool> AnyForDonationAndParticipantAsync(int donationId, int volunteerId, int ngoId);

        // שידוכים שממתינים לאישור מתנדב ושזמן התגובה שלהם עבר
        Task<List<Match>> GetExpiredAwaitingConfirmationAsync(DateTime asOfUtc);

        // שידוכים שאושרו אך לא נאספו עד הזמן שנקבע (לזיהוי No-Show)
        Task<List<Match>> GetOverdueNotPickedUpAsync(DateTime estimatedArrivalBeforeUtc);

        // השידוך הפעיל של התרומה, ואם אין - האחרון שנוצר
        Task<Match?> GetCurrentForDonationAsync(int donationId);

        // השידוכים של העמותה (האחרון לכל תרומה), כולל התרומה והמתנדב
        Task<List<Match>> GetLatestForNgoAsync(int ngoId);

        // תיקון 48: השידוך האחרון של כל תרומה של הספק, כולל העמותה והמתנדב (לדשבורד ולרשימות של הספק)
        Task<List<Match>> GetLatestForSupplierAsync(int supplierId);

        // תיקון 21: כמה ק"ג כבר הופנו לכל עמותה ביום מסוים: NgoId -> ק"ג. נספרים רק שידוכים חיים (ממתינים / אושרו /
        // באיסוף / בדרך) ושהושלמו - הצעה שנדחתה / לא נענתה / No-Show / בוטלה לא "תופסת" מקום בקיבולת העמותה.
        Task<Dictionary<int, double>> SumWeightKgByNgoOnDateAsync(IEnumerable<int> ngoIds, DateTime dayStartUtc, DateTime dayEndUtc);

        // כמה שידוכים (מכל הסוגים) נוצרו לכל עמותה ביום מסוים: NgoId -> מספר שידוכים
        Task<Dictionary<int, int>> CountByNgoOnDateAsync(IEnumerable<int> ngoIds, DateTime dayStartUtc, DateTime dayEndUtc);

        // תיקון 16: כל השיבוצים של מתנדב (גם שהסתיימו), מהחדש לישן - להיסטוריית השיבוצים שלו
        Task<List<Match>> GetHistoryForVolunteerAsync(int volunteerId, int take);

        // מתנדבים שיש להם כרגע הצעה או שיבוץ פעיל
        Task<HashSet<int>> GetVolunteerIdsWithActiveMatchesAsync();

        // מתנדבים שכבר דחו / לא הגיבו / לא הגיעו לתרומה הזו (לא מציעים להם אותה שוב)
        // תיקון 13: לכל אחת מהתרומות - המתנדבים שכבר דחו אותה / לא ענו בזמן / לא הגיעו (זוגות מתנדב-תרומה)
        Task<HashSet<(int VolunteerId, int DonationId)>> GetFailedVolunteerDonationPairsAsync(IReadOnlyCollection<int> donationIds);

        // כמה מסלולים שונים של המתנדב הסתיימו ב-No-Show
        Task<int> CountNoShowRoutesForVolunteerAsync(int volunteerId);

        Task<int> CountActiveAsync();
        Task<int> CountByStatusSinceAsync(MatchStatus status, DateTime sinceUtc);

        // תיקון 70: אירועי No-Show מאז מועד מסוים, יחד עם התרומה (כדי לספור רק את מה שאפשר עדיין לטפל בו)
        Task<List<Match>> GetNoShowsWithDonationSinceAsync(DateTime sinceUtc);
        Task<int> CountCreatedSinceAsync(DateTime sinceUtc);

        // תיקון 17: המתנדבים עם הכי הרבה משלוחים שהושלמו (Completed), מהמוביל ומטה. רק מי שהשלים לפחות משלוח אחד.
        Task<List<VolunteerDeliveriesCount>> GetTopVolunteersByDeliveriesAsync(int take);

        // תיקון 17: כמה זמן לקח למתנדבים לענות להצעות (אישור או דחייה). תשובה אחת לכל הצעה,
        // גם כשההצעה כוללת כמה תחנות. הצעה שלא נענתה בזמן (TimedOut) לא נספרת.
        Task<List<TimeSpan>> GetOfferResponseTimesAsync();

        // תיקון 77: האם יש תרומה בדרך לעמותה (שידוך פעיל שעוד לא הושלם, כולל מסירה שממתינה לאישור קבלה)
        Task<bool> HasOpenForNgoAsync(int ngoId);
    }

    // תיקון 17: שורה בדוח "מובילי ביצועי מתנדבים"
    public sealed record VolunteerDeliveriesCount(int VolunteerId, string? VolunteerName, int Deliveries);
}
