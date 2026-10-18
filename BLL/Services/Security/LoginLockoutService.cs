using DAL.Classes;
using DAL.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Security
{
    // נעילה אחרי ניסיונות התחברות כושלים - נשמר במסד (טבלת LoginAttempts), לא ב-MemoryCache.
    //
    // תוקן: בעבר הנעילה הייתה לפי המייל בלבד, כך שכל אחד יכול היה לנעול משתמש אחר
    // פשוט ב-5 ניסיונות כושלים עם המייל שלו. עכשיו:
    //   * 5 כישלונות לאותו מייל מאותה כתובת IP -> אותה כתובת IP נעולה לאותו מייל ל-15 דקות.
    //     המשתמש האמיתי (מ-IP אחר) ממשיך להתחבר כרגיל.
    //   * 20 כישלונות מאותה כתובת IP (לכל המיילים יחד) -> ה-IP נעול ל-15 דקות (מונע ניחוש המוני).
    public interface ILoginLockoutService
    {
        // null = לא נעול; אחרת - כמה זמן נשאר עד שהנעילה תסתיים
        Task<TimeSpan?> GetLockRemainingAsync(string email, string? ipAddress);

        Task RecordAsync(string email, string? ipAddress, int? userId, bool success, string? failureReason = null);
    }

    public sealed class LoginLockoutService : ILoginLockoutService
    {
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
        public const int MaxFailuresPerAccountAndIp = 5;
        public const int MaxFailuresPerIp = 20;

        private readonly ILoginAttemptRepository _repository;
        private readonly TimeProvider _clock;

        public LoginLockoutService(ILoginAttemptRepository repository, TimeProvider? clock = null)
        {
            _repository = repository;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<TimeSpan?> GetLockRemainingAsync(string email, string? ipAddress)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var since = now - Window;

            var accountFailures = await _repository.GetRecentFailuresAsync(email, ipAddress, since);
            TimeSpan? remaining = null;

            if (accountFailures.Count >= MaxFailuresPerAccountAndIp)
                remaining = Max(remaining, accountFailures.Max() + Window - now);

            if (!string.IsNullOrEmpty(ipAddress))
            {
                var ipFailures = await _repository.GetRecentFailuresByIpAsync(ipAddress, since, MaxFailuresPerIp);
                if (ipFailures.Count >= MaxFailuresPerIp)
                    remaining = Max(remaining, ipFailures.Max() + Window - now);
            }

            return remaining is { } r && r > TimeSpan.Zero ? r : null;
        }

        public Task RecordAsync(string email, string? ipAddress, int? userId, bool success, string? failureReason = null) =>
            _repository.AddAsync(new LoginAttempt
            {
                Email = email.Length <= 200 ? email : email.Substring(0, 200),
                IpAddress = ipAddress,
                UserId = userId,
                WasSuccessful = success,
                FailureReason = failureReason,
                AttemptedAt = _clock.GetUtcNow().UtcDateTime
            });

        private static TimeSpan? Max(TimeSpan? a, TimeSpan b) => a == null || b > a ? b : a;
    }
}
