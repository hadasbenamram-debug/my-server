using DAL.Classes;
using Microsoft.AspNetCore.Identity;
using System;

namespace BLL.Security
{
    // ה-PasswordHasher של Microsoft (מוסיף salt ומבצע אלפי סבבים של PBKDF2). במסד נשמר רק ה-hash.
    // תוספת אחת: hash בפורמט הישן של הפרויקט עדיין מאומת, ומחזיר SuccessRehashNeeded,
    // כך ש-UserService שומר מחדש את הסיסמה בפורמט של Microsoft בהתחברות הבאה.
    public sealed class PasswordHasherWithLegacySupport : IPasswordHasher<User>
    {
        private readonly PasswordHasher<User> _microsoftHasher = new();

        public string HashPassword(User user, string password) =>
            _microsoftHasher.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
                return PasswordVerificationResult.Failed;

            if (LegacyPasswordHash.IsLegacyFormat(hashedPassword))
            {
                return LegacyPasswordHash.Verify(providedPassword, hashedPassword)
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;
            }

            try
            {
                return _microsoftHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            }
            catch (FormatException)
            {
                // ערך פגום במסד - מתנהגים כמו סיסמה שגויה
                return PasswordVerificationResult.Failed;
            }
        }
    }
}
