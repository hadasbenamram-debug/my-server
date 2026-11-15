using System;

namespace FoodRescue.DTOs
{
    public class UserDto
    {
        public int Id { get; set; }

        // תוקן: היה Username, שדה שלא קיים ב-Entity (שם נקרא Name) ולכן תמיד חזר null
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // תיקון 10: כל התפקידים של החשבון (למשל Supplier + Volunteer)
        public System.Collections.Generic.List<string> Roles { get; set; } = new();
        public bool IsEmailVerified { get; set; }
        public bool IsPhoneVerified { get; set; }
        public DateTime CreatedAt { get; set; }

        // תיקון 77: חשבון שנמחק (מחיקה רכה) - המנהל רואה שהוא לא פעיל ומתי נמחק
        public bool IsActive { get; set; } = true;
        public DateTime? DeactivatedAt { get; set; }
    }

    // תיקון 51: מנהל מסמן ידנית שהמייל / הטלפון של משתמש אומתו (למשל אחרי שיחה איתו), או מבטל אימות.
    // שדה שלא נשלח (null) לא משתנה.
    public class UpdateVerificationDto
    {
        public bool? IsEmailVerified { get; set; }
        public bool? IsPhoneVerified { get; set; }
    }
}
