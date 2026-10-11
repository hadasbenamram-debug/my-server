using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Algorithms
{
    /// <summary>
    /// שירות Memoization גנרי עם TTL.
    ///
    /// תיקונים לעומת הגרסה המקורית (שתיארה את עצמה כ"אטומית"):
    ///  * ConcurrentDictionary.AddOrUpdate יכול להריץ את פונקציית היצירה יותר מפעם אחת במקביל –
    ///    כלומר כמה קריאות מקבילות ל-Google Maps לאותו מפתח. כאן כל ערך עטוף ב-Lazy, ולכן החישוב רץ פעם אחת.
    ///  * Task שנכשל נשמר במטמון עד סוף ה-TTL וגרם לכל הקוראים להיכשל. כאן ערך שנכשל מוסר מיד.
    ///  * המטמון (Singleton) לא נוקה מעולם – ערכים שפג תוקפם נשארו לנצח ("דליפת זיכרון"). כאן יש ניקוי אופורטוניסטי.
    /// </summary>
    public class MemoizationService<TResult>
    {
        private sealed class Entry<TValue>
        {
            public Lazy<TValue> Value { get; }
            public DateTime CreatedAtUtc { get; }
            public TimeSpan Ttl { get; }

            public Entry(Func<TValue> factory, DateTime createdAtUtc, TimeSpan ttl)
            {
                Value = new Lazy<TValue>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
                CreatedAtUtc = createdAtUtc;
                Ttl = ttl;
            }

            public bool IsFresh(DateTime nowUtc) => nowUtc - CreatedAtUtc < Ttl;
        }

        private const int SweepThreshold = 5000;

        private readonly ConcurrentDictionary<string, Entry<TResult>> _cache = new ConcurrentDictionary<string, Entry<TResult>>();
        private readonly ConcurrentDictionary<string, Entry<Task<TResult>>> _asyncCache = new ConcurrentDictionary<string, Entry<Task<TResult>>>();
        private readonly TimeSpan _defaultTtl;
        private int _sweeping;

        public MemoizationService(TimeSpan? defaultTtl = null)
        {
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(2);
        }

        public TResult GetOrCalculate(string cacheKey, Func<TResult> calculator, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                throw new ArgumentException("cacheKey לא יכול להיות ריק", nameof(cacheKey));
            if (calculator == null) throw new ArgumentNullException(nameof(calculator));

            var effectiveTtl = ttl ?? _defaultTtl;
            var now = DateTime.UtcNow;

            var entry = _cache.AddOrUpdate(
                cacheKey,
                _ => new Entry<TResult>(calculator, now, effectiveTtl),
                (_, existing) => existing.IsFresh(now) ? existing : new Entry<TResult>(calculator, now, effectiveTtl));

            try
            {
                return entry.Value.Value;
            }
            catch
            {
                _cache.TryRemove(new KeyValuePair<string, Entry<TResult>>(cacheKey, entry));
                throw;
            }
            finally
            {
                SweepIfNeeded(now);
            }
        }

        public async Task<TResult> GetOrCalculateAsync(string cacheKey, Func<Task<TResult>> calculator, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                throw new ArgumentException("cacheKey לא יכול להיות ריק", nameof(cacheKey));
            if (calculator == null) throw new ArgumentNullException(nameof(calculator));

            var effectiveTtl = ttl ?? _defaultTtl;
            var now = DateTime.UtcNow;

            var entry = _asyncCache.AddOrUpdate(
                cacheKey,
                _ => new Entry<Task<TResult>>(() => calculator(), now, effectiveTtl),
                (_, existing) => existing.IsFresh(now) ? existing : new Entry<Task<TResult>>(() => calculator(), now, effectiveTtl));

            try
            {
                return await entry.Value.Value.ConfigureAwait(false);
            }
            catch
            {
                // לא משאירים במטמון ערך שנכשל
                _asyncCache.TryRemove(new KeyValuePair<string, Entry<Task<TResult>>>(cacheKey, entry));
                throw;
            }
            finally
            {
                SweepIfNeeded(now);
            }
        }

        private void SweepIfNeeded(DateTime nowUtc)
        {
            if (_cache.Count + _asyncCache.Count < SweepThreshold) return;
            if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0) return;

            try
            {
                foreach (var kv in _cache)
                    if (!kv.Value.IsFresh(nowUtc)) _cache.TryRemove(kv);

                foreach (var kv in _asyncCache)
                    if (!kv.Value.IsFresh(nowUtc)) _asyncCache.TryRemove(kv);
            }
            finally
            {
                Volatile.Write(ref _sweeping, 0);
            }
        }

        /// <summary>בניית מפתח יציב: מספרים בפורמט Invariant (ללא תלות בתרבות השרת).</summary>
        public static string BuildKey(params object[] parts)
        {
            return string.Join("|", parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture) ?? string.Empty));
        }

        public void ClearCache()
        {
            _cache.Clear();
            _asyncCache.Clear();
        }

        public bool TryRemove(string cacheKey)
        {
            return _cache.TryRemove(cacheKey, out _) | _asyncCache.TryRemove(cacheKey, out _);
        }

        public void InvalidateByPrefix(string keyPrefix)
        {
            foreach (var key in _cache.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _cache.TryRemove(key, out _);

            foreach (var key in _asyncCache.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _asyncCache.TryRemove(key, out _);
        }

        public int Count => _cache.Count + _asyncCache.Count;
    }

    /// <summary>עטיפה ספציפית לתחום המרחקים.</summary>
    public class DistanceMemoizationService
    {
        private readonly MemoizationService<double> _inner;

        public DistanceMemoizationService(TimeSpan? defaultTtl = null)
        {
            _inner = new MemoizationService<double>(defaultTtl);
        }

        public double GetOrCalculateDistance(string cacheKey, Func<double> distanceCalculator)
            => _inner.GetOrCalculate(cacheKey, distanceCalculator);

        public Task<double> GetOrCalculateDistanceAsync(string cacheKey, Func<Task<double>> distanceCalculator)
            => _inner.GetOrCalculateAsync(cacheKey, distanceCalculator);

        public static string BuildKey(int volunteerId, int donationId) =>
            MemoizationService<double>.BuildKey(volunteerId, donationId);

        /// <summary>
        /// חדש: המפתח כולל את מיקום המתנדב (מעוגל ל-~100 מ'). המיקום מתעדכן ב-GPS בזמן אמת,
        /// ובלי זה מתנדב שזז קיבל מרחק ישן עד סוף ה-TTL.
        /// </summary>
        public static string BuildKey(int volunteerId, int donationId, double volunteerLat, double volunteerLon) =>
            MemoizationService<double>.BuildKey(volunteerId, donationId, Math.Round(volunteerLat, 3), Math.Round(volunteerLon, 3));

        public static string VolunteerPrefix(int volunteerId) => $"{volunteerId}|";

        public void InvalidateForVolunteer(int volunteerId) => _inner.InvalidateByPrefix(VolunteerPrefix(volunteerId));

        public void ClearCache() => _inner.ClearCache();
    }
}
