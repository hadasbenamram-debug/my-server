using System;

namespace FoodRescue.DTOs
{
    public class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        // absolute UTC expiry of Token.
        public DateTime ExpiresAt { get; set; }

        // true = המשתמש נכנס עם סיסמה זמנית וחייב להחליף אותה לפני כל פעולה אחרת
        public bool MustChangePassword { get; set; }
    }

    // תשובת POST api/auth/link-login (כניסה מקישור במייל האישור)
    public class LinkLoginResponseDto : AuthResponseDto
    {
        // true = עדיין לא נכנסו: צריך לבחור סיסמה אישית ולשלוח שוב עם NewPassword (הקישור עוד לא נוצל)
        public bool RequiresPassword { get; set; }

        // לאן להיכנס אחרי הכניסה (למתנדב: מסך הזמינות)
        public string? NextPath { get; set; }

        public string? Email { get; set; }
    }
}
