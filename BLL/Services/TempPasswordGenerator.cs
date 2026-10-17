using System;
using System.Security.Cryptography;

namespace BLL.Services
{
    // יוצר סיסמה זמנית למשתמש שנרשם דרך טופס ההרשמה של ספק / עמותה / מתנדב.
    // (הקוד היה מועתק בשלושה קונטרולרים - עכשיו יש לו מקום אחד.)
    public static class TempPasswordGenerator
    {
        public static string Generate() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(8))
                .Replace('+', 'A').Replace('/', 'B').TrimEnd('=') + "1!";
    }
}
