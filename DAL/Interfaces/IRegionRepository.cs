using DAL.Classes;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IRegionRepository : IRepository<Region>
    {
        Task<bool> ExistsAsync(int id);

        // מחיקה שמחזירה false אם המסד סירב (האזור עדיין מקושר לרשומות אחרות)
        Task<bool> TryDeleteAsync(Region region);
    }
}
