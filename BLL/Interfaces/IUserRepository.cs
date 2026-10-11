using BLL.Interfaces;
using DAL.Classes;

namespace BLL.Interfaces
{
    public interface IUserRepository : IRepository<User>
    {
        // כאן אפשר להוסיף שאילתות מיוחדות שקשורות רק למשתמשים, למשל:
        Task<User> GetByEmailAsync(string email);
    }
}