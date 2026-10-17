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
        public bool IsEmailVerified { get; set; }
        public bool IsPhoneVerified { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
