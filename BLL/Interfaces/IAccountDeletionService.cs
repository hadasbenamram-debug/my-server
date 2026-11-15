using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // תיקון 77: מחיקת החשבון של המשתמש המחובר (מחיקה רכה - IsActive = false)
    public interface IAccountDeletionService
    {
        /// <summary>
        /// מוחק (מסמן כלא פעיל) את החשבון. זורק:
        /// ArgumentException - סיסמה שגויה; ForbiddenException - חשבון מנהל;
        /// ConflictException - יש מסלול פעיל / תרומות בטיפול / תרומות בדרך לעמותה.
        /// </summary>
        Task DeleteMyAccountAsync(int userId, DeleteAccountDto dto);

        /// <summary>
        /// תיקון 78: מנהל משחזר חשבון שנמחק (למשל אחרי פנייה בדף צור קשר).
        /// זורק NotFoundException אם אין משתמש כזה, ו-ConflictException אם החשבון לא נמחק.
        /// </summary>
        Task RestoreAccountAsync(int userId);
    }
}
