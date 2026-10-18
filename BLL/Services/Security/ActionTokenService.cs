using DAL.Classes;
using DAL.Interfaces;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BLL.Security
{
    public enum CodeCheckResult
    {
        Valid,
        Invalid,
        Expired,      // אין קוד בתוקף (פג / נוצל / לא נשלח)
        TooManyAttempts
    }

    // טוקנים חד-פעמיים ומוגבלי-מטרה, שמורים במסד (Hash בלבד).
    // מחליף את ה-MemoryCache: שורד הפעלה מחדש של השרת ועובד גם כשיש כמה שרתים.
    public interface IActionTokenService
    {
        // טוקן אקראי לקישור (איפוס סיסמה / תגובה לשיבוץ). מחזיר את הטוקן הגלוי - לשליחה במייל בלבד.
        Task<string> CreateLinkTokenAsync(int? userId, ActionTokenPurpose purpose, TimeSpan lifetime, int? targetId = null);

        // קוד בן 6 ספרות (אימות מייל / טלפון). קוד קודם של אותה מטרה מבוטל.
        Task<string> CreateCodeAsync(int userId, ActionTokenPurpose purpose, TimeSpan lifetime);

        // טוקן קישור בתוקף (לא נוצל ולא פג), או null
        Task<ActionToken?> FindValidLinkTokenAsync(ActionTokenPurpose purpose, string rawToken);

        // בדיקת קוד; אחרי maxAttempts ניסיונות שגויים הקוד מבוטל. קוד נכון מסומן כמנוצל.
        Task<CodeCheckResult> VerifyCodeAsync(int userId, ActionTokenPurpose purpose, string code, int maxAttempts = 5);

        // הטוקן האחרון שנוצר (לבדיקת Cooldown)
        Task<DateTime?> GetLastIssuedAtAsync(int userId, ActionTokenPurpose purpose);

        // מסמן טוקן כמנוצל
        Task ConsumeAsync(ActionToken token);

        // מבטל את כל הטוקנים הפעילים של המשתמש למטרה (ולישות, אם צוינה)
        Task RevokeAsync(int? userId, ActionTokenPurpose purpose, int? targetId = null);

        // רישום ערך שהמשתמש יצר (למשל קובץ תמונה שהעלה) לצורך בדיקת בעלות בהמשך
        Task RegisterOwnedValueAsync(int userId, ActionTokenPurpose purpose, string value, TimeSpan lifetime);

        // האם הערך שייך למשתמש ועדיין בתוקף (ואם consume - מסמן אותו כמנוצל)
        Task<bool> TryUseOwnedValueAsync(int userId, ActionTokenPurpose purpose, string value, bool consume);
    }

    public sealed class ActionTokenService : IActionTokenService
    {
        private readonly IActionTokenRepository _repository;
        private readonly TimeProvider _clock;

        public ActionTokenService(IActionTokenRepository repository, TimeProvider? clock = null)
        {
            _repository = repository;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        // קודים קצרים מגובבים יחד עם מזהה המשתמש, כדי ששני משתמשים עם אותו קוד יקבלו Hash שונה
        private static string HashCode(int userId, string code) => Hash($"{userId}:{code.Trim()}");

        public async Task<string> CreateLinkTokenAsync(int? userId, ActionTokenPurpose purpose, TimeSpan lifetime, int? targetId = null)
        {
            var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            await _repository.AddAsync(new ActionToken
            {
                UserId = userId,
                Purpose = purpose,
                TokenHash = Hash(raw),
                TargetId = targetId,
                CreatedAt = Now,
                ExpiresAt = Now.Add(lifetime)
            });

            return raw;
        }

        public async Task<string> CreateCodeAsync(int userId, ActionTokenPurpose purpose, TimeSpan lifetime)
        {
            await _repository.ConsumeActiveAsync(userId, purpose, null, Now);

            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            await _repository.AddAsync(new ActionToken
            {
                UserId = userId,
                Purpose = purpose,
                TokenHash = HashCode(userId, code),
                CreatedAt = Now,
                ExpiresAt = Now.Add(lifetime)
            });

            return code;
        }

        public async Task<ActionToken?> FindValidLinkTokenAsync(ActionTokenPurpose purpose, string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200)
                return null;

            return await _repository.FindActiveByHashAsync(purpose, Hash(rawToken.Trim()), Now);
        }

        public async Task<CodeCheckResult> VerifyCodeAsync(int userId, ActionTokenPurpose purpose, string code, int maxAttempts = 5)
        {
            var token = await _repository.GetLatestForUserAsync(userId, purpose);
            if (token == null || token.ConsumedAt != null || token.ExpiresAt <= Now)
                return CodeCheckResult.Expired;

            if (string.IsNullOrWhiteSpace(code) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(token.TokenHash),
                    Encoding.ASCII.GetBytes(HashCode(userId, code))))
            {
                token.FailedAttempts++;
                if (token.FailedAttempts >= maxAttempts)
                {
                    token.ConsumedAt = Now;
                    await _repository.UpdateAsync(token);
                    return CodeCheckResult.TooManyAttempts;
                }

                await _repository.UpdateAsync(token);
                return CodeCheckResult.Invalid;
            }

            token.ConsumedAt = Now;
            await _repository.UpdateAsync(token);
            return CodeCheckResult.Valid;
        }

        public async Task<DateTime?> GetLastIssuedAtAsync(int userId, ActionTokenPurpose purpose) =>
            (await _repository.GetLatestForUserAsync(userId, purpose))?.CreatedAt;

        public async Task ConsumeAsync(ActionToken token)
        {
            token.ConsumedAt = Now;
            await _repository.UpdateAsync(token);
        }

        public Task RevokeAsync(int? userId, ActionTokenPurpose purpose, int? targetId = null) =>
            _repository.ConsumeActiveAsync(userId, purpose, targetId, Now);

        public Task RegisterOwnedValueAsync(int userId, ActionTokenPurpose purpose, string value, TimeSpan lifetime) =>
            _repository.AddAsync(new ActionToken
            {
                UserId = userId,
                Purpose = purpose,
                TokenHash = Hash(value),
                TargetValue = value.Length <= 260 ? value : value.Substring(0, 260),
                CreatedAt = Now,
                ExpiresAt = Now.Add(lifetime)
            });

        public async Task<bool> TryUseOwnedValueAsync(int userId, ActionTokenPurpose purpose, string value, bool consume)
        {
            var token = await _repository.FindActiveForUserAsync(userId, purpose, value, Now);
            if (token == null) return false;

            if (consume)
                await ConsumeAsync(token);

            return true;
        }
    }
}
