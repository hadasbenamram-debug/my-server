using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DAL.Classes
{
    public enum UserRole
    {
        User,
        Admin,
        Supplier,
        Volunteer,
        Ngo
    }
    public class User

    {

        [Key]

        public int Id { get; set; }

        [Required, MaxLength(100)] public string Name { get; set; }

        [Required, EmailAddress] public string Email { get; set; }

        [Required, MaxLength(20)] public string Phone { get; set; }

        // נוסף: תמיכה בהתחברות אמיתית מול מסד הנתונים (ראו PasswordHasher +
        // AuthController.Login/Register). לפני כן AuthController היה משווה מול
        // מייל/סיסמה קשיחים בקוד, בלי שום קשר לטבלת המשתמשים בפועל. השדה אופציונלי
        // (nullable) כדי לא לשבור רשומות שנוצרות דרך UsersController הישן בלי סיסמה.
        [MaxLength(200)]
        public string PasswordHash { get; set; }



        // נוסף: אימות טלפון/מייל - חובה לפי תהליך ההרשמה

        public bool IsEmailVerified { get; set; } = false;

        public bool IsPhoneVerified { get; set; } = false;



        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;



        public UserRole Role { get; set; } = UserRole.User; // ברירת מחדל: משתמש רגיל
        public Supplier SupplierProfile { get; set; }

        public Ngo NgoProfile { get; set; }

        public Volunteer VolunteerProfile { get; set; }

        public Manager ManagerProfile { get; set; }



        // נוסף: כל בקשות האישור שהוגשו על ידי המשתמש הזה

        public ICollection<ApprovalRequest> ApprovalRequests { get; set; }

        // נוסף: לוג כל ההודעות שנשלחו למשתמש

        public ICollection<Notification> Notifications { get; set; }

        // חותמת אבטחה: נכנסת לכל JWT ומתחלפת בכל שינוי/איפוס סיסמה.
        // טוקן שהונפק לפני השינוי מכיל חותמת ישנה ולכן נדחה (ראו Program.cs - OnTokenValidated).
        [Required, MaxLength(64)]
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

        // המשתמש נרשם עם סיסמה זמנית שנשלחה במייל - חייב להחליף אותה לפני שימוש במערכת.
        public bool MustChangePassword { get; set; }

        // הנעילה אחרי ניסיונות כושלים נשמרת בטבלת LoginAttempts (לפי מייל + IP),
        // ולכן אין יותר שדה LockoutEnd על המשתמש.

    }
}