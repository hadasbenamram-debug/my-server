using DAL.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Configuration
{
    // המדיניות בפועל = ערכי appsettings + מה שהמנהל שמר ב-api/settings (טבלת SystemSettings).
    public interface IMatchingPolicyProvider
    {
        MatchingPolicyOptions Current { get; }

        // טוען מחדש את ההגדרות מהמסד (נקרא בעליית השרת, אחרי שמירה, ובתחילת כל ריצת מנוע)
        Task ReloadAsync(CancellationToken ct = default);
    }

    public sealed class MatchingPolicyProvider : IMatchingPolicyProvider
    {
        public const string KeyPrefix = "MatchingPolicy:";

        private readonly IOptionsMonitor<MatchingPolicyOptions> _defaults;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MatchingPolicyProvider> _logger;
        private volatile IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>();

        public MatchingPolicyProvider(
            IOptionsMonitor<MatchingPolicyOptions> defaults,
            IServiceScopeFactory scopeFactory,
            ILogger<MatchingPolicyProvider> logger)
        {
            _defaults = defaults;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public MatchingPolicyOptions Current => Apply(_defaults.CurrentValue.Clone(), _overrides, _logger);

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
                var rows = await repo.GetAllAsync();

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    if (row.Key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
                        dict[row.Key.Substring(KeyPrefix.Length)] = row.Value;
                }

                _overrides = dict;
            }
            catch (Exception ex)
            {
                // אם המסד לא זמין - ממשיכים עם ההגדרות האחרונות שנטענו
                _logger.LogWarning(ex, "Failed to reload matching policy overrides; keeping the previous values.");
            }
        }

        // מחיל ערכים שנשמרו כמחרוזות על אובייקט המדיניות (רק מאפיינים מוכרים, עם בדיקת טיפוס)
        public static MatchingPolicyOptions Apply(MatchingPolicyOptions target, IReadOnlyDictionary<string, string> overrides, ILogger? logger = null)
        {
            foreach (var (name, raw) in overrides)
            {
                var prop = typeof(MatchingPolicyOptions).GetProperty(name);
                if (prop == null || !prop.CanWrite) continue;

                try
                {
                    object value = prop.PropertyType == typeof(int)
                        ? int.Parse(raw, CultureInfo.InvariantCulture)
                        : prop.PropertyType == typeof(double)
                            ? double.Parse(raw, CultureInfo.InvariantCulture)
                            : raw;
                    prop.SetValue(target, value);
                }
                catch (FormatException)
                {
                    logger?.LogWarning("Ignoring invalid value '{Value}' for setting {Name}.", raw, name);
                }
            }

            return target;
        }
    }
}

namespace BLL.Configuration
{
    // מדיניות קבועה (לבדיקות ולשימושים שאינם תלויים במסד)
    public sealed class StaticMatchingPolicyProvider : IMatchingPolicyProvider
    {
        private readonly MatchingPolicyOptions _options;

        public StaticMatchingPolicyProvider(MatchingPolicyOptions? options = null)
        {
            _options = options ?? new MatchingPolicyOptions();
        }

        public MatchingPolicyOptions Current => _options.Clone();

        public System.Threading.Tasks.Task ReloadAsync(System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
