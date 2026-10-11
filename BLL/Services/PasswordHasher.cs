using System;
using System.Security.Cryptography;

namespace BLL.Algorithms
{
    /// <summary>
    /// נוסף: תוקן חוסר קריטי - עד כה AuthController.Login השווה מייל/סיסמה מול
    /// ערכים קשיחים בקוד, ו-User בכלל לא היה לו שדה לשמירת סיסמה. מימוש זה משתמש
    /// ב-Rfc2898DeriveBytes (PBKDF2-HMACSHA256) המובנה ב-.NET - לא נדרשת תלות
    /// חיצונית (כמו BCrypt.Net) שלא הייתה מוגדרת ב-csproj ממילא. כל האש כולל את
    /// ה-salt ומספר האיטרציות בתוכו (פורמט "iterations.saltBase64.hashBase64"),
    /// כך שאפשר להעלות בעתיד את מספר האיטרציות בלי לשבור אימות של האש-ים הישנים.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int DefaultIterations = 100_000;

        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("סיסמה לא יכולה להיות ריקה", nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSizeBytes);

            return $"{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            var parts = storedHash.Split('.', 3);
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out int iterations)) return false;

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedHash = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
