using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class MatchRepository : Repository<Match>, IMatchRepository
    {
        public MatchRepository(FoodRescueDbContext context) : base(context) { }

        private static readonly MatchStatus[] ActiveStatuses =
        {
            MatchStatus.AwaitingVolunteerConfirmation,
            MatchStatus.Confirmed,
            MatchStatus.InPickup,
            MatchStatus.InTransit
        };

        // שידוך עם כל מה שצריך כדי להציג אותו (כתובות, שמות, קואורדינטות)
        private IQueryable<Match> WithDetails() => _context.Matches
            .Include(m => m.Donation).ThenInclude(d => d.Supplier)
            .Include(m => m.Ngo)
            .Include(m => m.Volunteer).ThenInclude(v => v.User);

        public async Task<List<Match>> GetActiveMatchesAsync()
        {
            return await WithDetails()
                .Where(m => m.IsActive && ActiveStatuses.Contains(m.Status))
                .OrderBy(m => m.AssignedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // תיקון 16: ההיסטוריה כוללת גם שיבוצים שהסתיימו (Completed / Declined / TimedOut...) - לא רק פעילים.
        // הצעה שעדיין ממתינה לתשובה מופיעה ב"בקשה נכנסת", לא בהיסטוריה.
        public async Task<List<Match>> GetHistoryForVolunteerAsync(int volunteerId, int take)
        {
            return await WithDetails()
                .Where(m => m.VolunteerId == volunteerId &&
                            !(m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation))
                .OrderByDescending(m => m.DeliveredAt ?? m.RespondedAt ?? m.AssignedAt)
                .ThenByDescending(m => m.Id)
                .Take(take)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Match>> GetActiveForParticipantAsync(int volunteerId, int ngoId, int supplierId)
        {
            return await WithDetails()
                .Where(m => m.IsActive && ActiveStatuses.Contains(m.Status))
                .Where(m => m.VolunteerId == volunteerId || m.NgoId == ngoId || m.Donation.SupplierId == supplierId)
                .OrderBy(m => m.EstimatedArrival ?? m.AssignedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<PagedResult<Match>> GetPageAsync(int skip, int take, MatchStatus? status)
        {
            var query = WithDetails().AsNoTracking();
            if (status.HasValue)
                query = query.Where(m => m.Status == status.Value);
            return PageAsync(query.OrderByDescending(m => m.AssignedAt), skip, take);
        }

        public async Task<bool> HasActiveConfirmedMatchAsync(int volunteerId, int excludeMatchId, int? excludeRouteId = null)
        {
            return await _context.Matches.AnyAsync(m =>
                m.VolunteerId == volunteerId &&
                m.Id != excludeMatchId &&
                (excludeRouteId == null || m.VolunteerRouteId != excludeRouteId) &&
                (m.Status == MatchStatus.Confirmed ||
                 m.Status == MatchStatus.InPickup ||
                 m.Status == MatchStatus.InTransit));
        }

        public async Task<Match?> GetByIdWithDonationAsync(int id)
        {
            return await _context.Matches
                .Include(m => m.Donation)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<Match?> GetByIdWithDetailsAsync(int id)
        {
            return await WithDetails()
                .Include(m => m.VolunteerRoute)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<List<Match>> GetByRouteIdAsync(int routeId)
        {
            return await WithDetails()
                .Where(m => m.VolunteerRouteId == routeId)
                .OrderBy(m => m.EstimatedArrival)
                .ToListAsync();
        }

        public async Task<Match?> GetOwnedByVolunteerAsync(int id, int volunteerId)
        {
            return await WithDetails()
                .Include(m => m.VolunteerRoute)
                .FirstOrDefaultAsync(m => m.Id == id && m.VolunteerId == volunteerId);
        }

        public async Task<List<Match>> GetActiveByRouteForVolunteerAsync(int routeId, int volunteerId)
        {
            return await WithDetails()
                .Where(m => m.VolunteerRouteId == routeId && m.VolunteerId == volunteerId && m.IsActive)
                .OrderBy(m => m.EstimatedArrival)
                .ToListAsync();
        }

        public async Task<bool> AnyForDonationAndParticipantAsync(int donationId, int volunteerId, int ngoId)
        {
            return await _context.Matches.AnyAsync(m =>
                m.DonationId == donationId && (m.VolunteerId == volunteerId || m.NgoId == ngoId));
        }

        public async Task<List<Match>> GetExpiredAwaitingConfirmationAsync(DateTime asOfUtc)
        {
            return await _context.Matches
                .Include(m => m.Donation)
                .Include(m => m.Volunteer)
                .Include(m => m.Ngo)
                .Where(m => m.Status == MatchStatus.AwaitingVolunteerConfirmation
                         && m.ResponseDeadline != null
                         && m.ResponseDeadline <= asOfUtc)   // תיקון 38: כולל הרגע עצמו, כמו בבדיקת האישור
                .ToListAsync();
        }

        public async Task<List<Match>> GetOverdueNotPickedUpAsync(DateTime estimatedArrivalBeforeUtc)
        {
            return await _context.Matches
                .Include(m => m.Donation)
                .Where(m => m.IsActive
                         && (m.Status == MatchStatus.Confirmed || m.Status == MatchStatus.InPickup)
                         && m.PickedUpAt == null
                         && m.EstimatedArrival != null
                         && m.EstimatedArrival < estimatedArrivalBeforeUtc)
                .ToListAsync();
        }

        public async Task<Match?> GetCurrentForDonationAsync(int donationId)
        {
            return await WithDetails()
                .Include(m => m.VolunteerRoute)
                .Where(m => m.DonationId == donationId)
                .OrderByDescending(m => m.IsActive)
                .ThenByDescending(m => m.AssignedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<List<Match>> GetLatestForNgoAsync(int ngoId)
        {
            // השידוך האחרון לכל תרומה (ניסיונות קודמים שנדחו נשארים כהיסטוריה)
            var latestIds = _context.Matches
                .Where(m => m.NgoId == ngoId)
                .GroupBy(m => m.DonationId)
                .Select(g => g.Max(m => m.Id));

            return await _context.Matches
                .Include(m => m.Donation).ThenInclude(d => d.DonationFoodCategories).ThenInclude(c => c.Category)
                .Include(m => m.Donation).ThenInclude(d => d.Region)
                .Include(m => m.Donation).ThenInclude(d => d.Supplier)
                .Include(m => m.Volunteer).ThenInclude(v => v.User)
                .Where(m => latestIds.Contains(m.Id))
                .OrderByDescending(m => m.AssignedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // תיקון 21: הסטטוסים שתופסים מקום בקיבולת היומית של העמותה
        private static readonly MatchStatus[] CapacityStatuses =
        {
            MatchStatus.AwaitingVolunteerConfirmation,
            MatchStatus.Confirmed,
            MatchStatus.InPickup,
            MatchStatus.InTransit,
            MatchStatus.Completed
        };

        public async Task<Dictionary<int, double>> SumWeightKgByNgoOnDateAsync(IEnumerable<int> ngoIds, DateTime dayStartUtc, DateTime dayEndUtc)
        {
            var ids = ngoIds.ToList();

            var rows = await _context.Matches
                .Where(m => ids.Contains(m.NgoId) && m.AssignedAt >= dayStartUtc && m.AssignedAt < dayEndUtc &&
                            CapacityStatuses.Contains(m.Status))
                .SelectMany(m => m.Donation.DonationFoodCategories.Select(c => new { m.NgoId, c.WeightKg }))
                .ToListAsync();

            return rows
                .GroupBy(r => r.NgoId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.WeightKg));
        }

        public async Task<Dictionary<int, int>> CountByNgoOnDateAsync(IEnumerable<int> ngoIds, DateTime dayStartUtc, DateTime dayEndUtc)
        {
            var ids = ngoIds.ToList();

            var loadList = await _context.Matches
                .Where(m => ids.Contains(m.NgoId) && m.AssignedAt >= dayStartUtc && m.AssignedAt < dayEndUtc)
                .GroupBy(m => m.NgoId)
                .Select(g => new { NgoId = g.Key, Count = g.Count() })
                .ToListAsync();

            return loadList.ToDictionary(x => x.NgoId, x => x.Count);
        }

        public async Task<HashSet<int>> GetVolunteerIdsWithActiveMatchesAsync()
        {
            var ids = await _context.Matches
                .Where(m => m.IsActive && ActiveStatuses.Contains(m.Status))
                .Select(m => m.VolunteerId)
                .Distinct()
                .ToListAsync();
            return ids.ToHashSet();
        }

        // תיקון 13: שאילתה אחת לכל הריצה (במקום שאילתה לכל תרומה), וגם כדי שמתנדב לא יקבל שוב תרומה
        // שכבר דחה / לא ענה עליה - גם לא כתחנה נוספת במסלול של תרומה אחרת
        public async Task<HashSet<(int VolunteerId, int DonationId)>> GetFailedVolunteerDonationPairsAsync(IReadOnlyCollection<int> donationIds)
        {
            if (donationIds.Count == 0)
                return new HashSet<(int VolunteerId, int DonationId)>();

            var pairs = await _context.Matches
                .Where(m => donationIds.Contains(m.DonationId) &&
                            (m.Status == MatchStatus.Declined ||
                             m.Status == MatchStatus.TimedOut ||
                             m.Status == MatchStatus.NoShow))
                .Select(m => new { m.VolunteerId, m.DonationId })
                .Distinct()
                .ToListAsync();
            return pairs.Select(p => (p.VolunteerId, p.DonationId)).ToHashSet();
        }

        public async Task<int> CountNoShowRoutesForVolunteerAsync(int volunteerId)
        {
            // מסלול עם כמה תחנות שסומן No-Show נספר פעם אחת
            return await _context.Matches
                .Where(m => m.VolunteerId == volunteerId && m.Status == MatchStatus.NoShow)
                .Select(m => m.VolunteerRouteId ?? -m.Id)
                .Distinct()
                .CountAsync();
        }

        public async Task<int> CountActiveAsync()
        {
            return await _context.Matches.CountAsync(m => m.IsActive && ActiveStatuses.Contains(m.Status));
        }

        public async Task<int> CountByStatusSinceAsync(MatchStatus status, DateTime sinceUtc)
        {
            return await _context.Matches.CountAsync(m => m.Status == status && m.AssignedAt >= sinceUtc);
        }

        public async Task<int> CountCreatedSinceAsync(DateTime sinceUtc)
        {
            return await _context.Matches.CountAsync(m => m.AssignedAt >= sinceUtc);
        }

        // תיקון 17: משלוח מוצלח = שיבוץ שהושלם (המתנדב מסר את התרומה לעמותה)
        public async Task<List<VolunteerDeliveriesCount>> GetTopVolunteersByDeliveriesAsync(int take)
        {
            var rows = await _context.Matches
                .Where(m => m.Status == MatchStatus.Completed)
                .GroupBy(m => new { m.VolunteerId, m.Volunteer.User.Name })
                .Select(g => new { g.Key.VolunteerId, g.Key.Name, Deliveries = g.Count() })
                .OrderByDescending(x => x.Deliveries)
                .ThenBy(x => x.VolunteerId)
                .Take(take)
                .ToListAsync();

            return rows.Select(x => new VolunteerDeliveriesCount(x.VolunteerId, x.Name, x.Deliveries)).ToList();
        }

        public async Task<List<TimeSpan>> GetOfferResponseTimesAsync()
        {
            var rows = await _context.Matches
                .Where(m => m.RespondedAt != null)
                .Select(m => new { m.Id, m.VolunteerRouteId, m.AssignedAt, RespondedAt = m.RespondedAt!.Value })
                .ToListAsync();

            // הצעה של כמה תחנות (אותו מסלול) נענית בפעולה אחת - כל התחנות שלה מקבלות אותו זמן תשובה
            return rows
                .GroupBy(r => new { Offer = r.VolunteerRouteId ?? -r.Id, r.RespondedAt })
                .Select(g => g.Key.RespondedAt - g.Min(r => r.AssignedAt))
                .Where(t => t >= TimeSpan.Zero)
                .ToList();
        }
    }
}
