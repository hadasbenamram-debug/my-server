using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<List<User>> GetByRoleAsync(UserRole role)
        {
            return await _context.Users
                .Where(u => u.Role == role)
                .ToListAsync();
        }

        public Task<PagedResult<User>> GetPageAsync(int skip, int take)
        {
            var query = _context.Users.OrderBy(u => u.Id).AsNoTracking();
            return PageAsync(query, skip, take);
        }

        public async Task<UserAuthState?> GetAuthStateAsync(int userId)
        {
            return await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new UserAuthState(u.SecurityStamp, u.MustChangePassword, u.Role))
                .FirstOrDefaultAsync();
        }

        public async Task<bool> TryAddAsync(User user)
        {
            try
            {
                await AddAsync(user);
                return true;
            }
            catch (DbUpdateException)
            {
                // שתי הרשמות בו-זמנית עם אותו אימייל נתקלו באינדקס הייחודי על Email.
                // מנתקים את הישות שנכשלה כדי שלא תישלח שוב ב-SaveChanges הבא.
                _context.Entry(user).State = EntityState.Detached;

                if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                    return false;

                throw;
            }
        }
    }
}
