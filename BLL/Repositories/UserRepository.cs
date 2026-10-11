using BLL.Interfaces;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;

namespace BLL.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<User> GetByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }
    }
}