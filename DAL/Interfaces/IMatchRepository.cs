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

        // כל השידוכים של מסלול (במעקב, כולל תרומה/מתנדב/עמותה)
        Task<List<Match>> GetByRouteIdAsync(int routeId);

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

        // כמה שידוכים יש לכל עמותה ביום מסוים: NgoId -> מספר שידוכים
        Task<Dictionary<int, int>> CountByNgoOnDateAsync(IEnumerable<int> ngoIds, DateTime dayStartUtc, DateTime dayEndUtc);

        // מתנדבים שיש להם כרגע הצעה או שיבוץ פעיל
        Task<HashSet<int>> GetVolunteerIdsWithActiveMatchesAsync();

        // מתנדבים שכבר דחו / לא הגיבו / לא הגיעו לתרומה הזו (לא מציעים להם אותה שוב)
        Task<HashSet<int>> GetVolunteerIdsWhoFailedDonationAsync(int donationId);

        // כמה מסלולים שונים של המתנדב הסתיימו ב-No-Show
        Task<int> CountNoShowRoutesForVolunteerAsync(int volunteerId);

        Task<int> CountActiveAsync();
        Task<int> CountByStatusSinceAsync(MatchStatus status, DateTime sinceUtc);
        Task<int> CountCreatedSinceAsync(DateTime sinceUtc);
    }
}
