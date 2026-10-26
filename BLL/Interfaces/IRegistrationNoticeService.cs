using DAL.Classes;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // תוצאת הרשמה. הקונטרולר מחזיר ללקוח בדיוק את אותה תשובה בשני המקרים (לא חושפים אילו מיילים רשומים).
    public enum RegistrationResult
    {
        Created,          // נוצר חשבון חדש (נשלח מייל עם סיסמה זמנית)
        ExistingAccount   // המייל כבר רשום: לא נוצר כלום, ובעל החשבון קיבל מייל
    }

    // תיקון 10: משתמש מחובר מוסיף לחשבון שלו תפקיד נוסף (מתנדב / ספק / עמותה) - אותו מייל, אותה סיסמה.
    // תוצאה ולא חריגה: המקרים הצפויים לא "מפילים" את השרת בדיבאגר.
    public enum AddRoleResult
    {
        Added,                  // נוצר פרופיל שממתין לאישור מנהל
        AlreadyHasRole,         // כבר יש לחשבון פרופיל כזה (מאושר / ממתין / נדחה)
        NotAvailableForAdmin,   // לחשבון מנהל לא מוסיפים תפקידים
        UserNotFound
    }

    public interface IRegistrationNoticeService
    {
        // מייל לבעל החשבון הקיים: "ניסו להירשם עם המייל שלך - כבר יש לך חשבון"
        Task NotifyExistingAccountAsync(User existingUser, string? roleTitle);

        // תיקון 10: אישור קבלה לבקשה להוסיף תפקיד לחשבון קיים ("הבקשה שלך להצטרף גם כמתנדב/ת התקבלה")
        Task NotifyRoleRequestReceivedAsync(User user, string roleTitle);
    }
}
