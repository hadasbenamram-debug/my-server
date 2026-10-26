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

    public interface IRegistrationNoticeService
    {
        // מייל לבעל החשבון הקיים: "ניסו להירשם עם המייל שלך - כבר יש לך חשבון"
        Task NotifyExistingAccountAsync(User existingUser, string? roleTitle);
    }
}
