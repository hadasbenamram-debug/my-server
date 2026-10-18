using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    // מה שנדרש כדי לאמת JWT בכל בקשה (בלי לטעון את כל הישות)
    public sealed record UserAuthState(string SecurityStamp, bool MustChangePassword, UserRole Role);

    public interface IUserRepository : IRepository<User>
    {
        Task<User?> GetByEmailAsync(string email);
        Task<List<User>> GetByRoleAsync(UserRole role);
        Task<PagedResult<User>> GetPageAsync(int skip, int take);
        Task<UserAuthState?> GetAuthStateAsync(int userId);

        // מחזיר false אם כבר קיים משתמש עם אותו אימייל (למשל נרשם באותו רגע בבקשה אחרת).
        Task<bool> TryAddAsync(User user);
    }
}
