using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<Supplier?> GetByUserIdAsync(int userId);

        // ספק עם המזהה הזה *וגם* של המשתמש הזה (null = לא קיים או לא שלו)
        Task<Supplier?> GetOwnedAsync(int id, int userId);
        Task<List<Supplier>> GetAllWithUserAsync();
    }
}
