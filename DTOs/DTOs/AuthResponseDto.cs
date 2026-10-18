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
}
