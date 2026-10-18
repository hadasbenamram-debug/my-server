using System;
using System.Security.Cryptography;

namespace BLL.Security
{
    // הפורמט הישן של ה-hash בפרויקט ("iterations.saltBase64.hashBase64", PBKDF2-HMACSHA256).
    // סיסמאות חדשות נשמרות רק עם ה-PasswordHasher של Microsoft. המחלקה הזו נשארת רק כדי שמשתמשים
    // שנרשמו לפני המעבר יוכלו להתחבר; בהתחברות הראשונה הסיסמה שלהם נשמרת מחדש בפורמט החדש.
    internal static class LegacyPasswordHash
    {
        // ב-Base64 (הפורמט של Microsoft) אין נקודות, ולכן נקודה מסמנת את הפורמט הישן
        public static bool IsLegacyFormat(string storedHash) =>
            !string.IsNullOrEmpty(storedHash) && storedHash.Contains('.');

        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            var parts = storedHash.Split('.', 3);
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out int iterations) || iterations <= 0) return false;

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
