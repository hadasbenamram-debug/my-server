using BLL.Interfaces;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Services
{
    // Service עזר: הלוגיקה של "האם הטוקן עדיין בתוקף" - השוואת החותמת שבטוקן לחותמת שבמסד.
    // ה-Repository רק שולף את המצב; ההחלטה נעשית כאן.
    public sealed class SessionValidationService : ISessionValidationService
    {
        private readonly IUserRepository _users;

        public SessionValidationService(IUserRepository users)
        {
            _users = users;
        }

        public async Task<SessionStateDto?> ValidateAsync(int userId, string? tokenSecurityStamp)
        {
            if (string.IsNullOrEmpty(tokenSecurityStamp))
                return null;

            var state = await _users.GetAuthStateAsync(userId);
            if (state == null || state.SecurityStamp != tokenSecurityStamp)
                return null;

            return new SessionStateDto
            {
                Role = state.Role.ToString(),
                MustChangePassword = state.MustChangePassword
            };
        }
    }
}
