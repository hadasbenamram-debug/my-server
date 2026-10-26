using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    public class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;

        // התפקיד שבו נכנסים (המסכים שהלקוח מציג)
        public string Role { get; set; } = string.Empty;

        // תיקון 10: כל התפקידים המאושרים של החשבון (למשל ספק שהוא גם מתנדב). הלקוח מאפשר לעבור ביניהם.
        public List<string> Roles { get; set; } = new();

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
