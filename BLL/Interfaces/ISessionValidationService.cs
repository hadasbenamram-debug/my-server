using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // בדיקת תוקף של טוקן מול המסד (חותמת האבטחה). נקרא מה-JWT middleware בשכבת ה-API,
    // כך שה-API לא ניגש ישירות ל-Repository.
    public interface ISessionValidationService
    {
        // null = המשתמש לא קיים, או שהחותמת בטוקן כבר לא תואמת (הסיסמה הוחלפה / אופסה)
        Task<SessionStateDto?> ValidateAsync(int userId, string? tokenSecurityStamp);
    }
}
