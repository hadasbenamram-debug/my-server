using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    // פעולות הגישה הבסיסיות שכל Repository מספק.
    // Repository אחראי רק על "איך ניגשים למידע": הוא מקבל ומחזיר Entities,
    // לא מכיר DTOs ולא מכיל חוקים עסקיים.
    public interface IRepository<T> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();
        Task<T> GetByIdAsync(int id);
        Task AddAsync(T entity);

        // שומר את השינויים בישות. ישות שכבר במעקב נשמרת כמו שהיא (רק העמודות שהשתנו);
        // ישות מנותקת מסומנת כמשתנה - היא בלבד, בלי הישויות הקשורות אליה.
        Task UpdateAsync(T entity);
        Task DeleteAsync(int id);
    }

    // עמוד אחד של תוצאות + סך כל הרשומות (לכותרת X-Total-Count)
    public sealed record PagedResult<T>(List<T> Items, int TotalCount);
}
