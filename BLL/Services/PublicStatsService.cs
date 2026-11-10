using BLL.Algorithms;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace BLL.Services
{
    /// <summary>
    /// תיקון 31: נתוני "ההשפעה שלנו" בסוף דף הבית. בעבר המספרים היו קבועים בקוד הלקוח (src/data.ts).
    /// עכשיו הם נספרים מהמסד: תרומות שהגיעו ליעדן, ק"ג מזון (מצטבר ומתחילת החודש), ספקים, מתנדבים ועמותות מאושרים.
    ///
    /// ה-endpoint פתוח לכולם (דף הבית), ולכן התוצאה נשמרת במטמון ל-CacheDuration: גם אם הדף נטען אלפי פעמים,
    /// המסד נשאל לכל היותר פעם אחת בכמה דקות.
    /// </summary>
    public class PublicStatsService : IPublicStatsService
    {
        public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private const string CacheKey = "public-stats";

        private readonly IDonationRepository _donations;
        private readonly ISupplierRepository _suppliers;
        private readonly IVolunteerRepository _volunteers;
        private readonly INgoRepository _ngos;
        private readonly IMemoryCache _cache;
        private readonly TimeProvider _clock;

        public PublicStatsService(
            IDonationRepository donations,
            ISupplierRepository suppliers,
            IVolunteerRepository volunteers,
            INgoRepository ngos,
            IMemoryCache cache,
            TimeProvider? clock = null)
        {
            _donations = donations;
            _suppliers = suppliers;
            _volunteers = volunteers;
            _ngos = ngos;
            _cache = cache;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<PublicStatsDto> GetAsync()
        {
            if (_cache.TryGetValue(CacheKey, out PublicStatsDto? cached) && cached != null)
                return cached;

            var stats = await ComputeAsync();
            _cache.Set(CacheKey, stats, CacheDuration);
            return stats;
        }

        private async Task<PublicStatsDto> ComputeAsync()
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            // תחילת החודש לפי שעון ישראל (ולא לפי UTC - אחרת "החודש" מתחיל ב-02:00/03:00 בלילה)
            var localNow = LocalClock.ToLocal(now);
            var monthStartUtc = LocalClock.ToUtc(new DateTime(localNow.Year, localNow.Month, 1));

            // השאילתות רצות אחת אחרי השנייה: DbContext אחד לא תומך בשאילתות מקבילות
            return new PublicStatsDto
            {
                DonationsDelivered = await _donations.CountDeliveredAsync(),
                KgRescued = Math.Round(await _donations.SumDeliveredWeightKgAsync(), 1),
                KgRescuedThisMonth = Math.Round(await _donations.SumDeliveredWeightKgSinceAsync(monthStartUtc), 1),
                PartnerBusinesses = await _suppliers.CountByApprovalStatusAsync(ApprovalStatus.Approved),
                ActiveVolunteers = await _volunteers.CountByApprovalStatusAsync(ApprovalStatus.Approved),
                PartnerNgos = await _ngos.CountByApprovalStatusAsync(ApprovalStatus.Approved),
                GeneratedAt = now
            };
        }
    }
}
