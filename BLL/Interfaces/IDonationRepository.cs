using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IDonationRepository : IRepository<Donation>
    {
        Task<IEnumerable<Donation>> GetAvailableDonationsAsync();
        Task<IEnumerable<Donation>> GetAllWithCategoriesAsync();
        Task<Donation?> GetByIdWithCategoriesAsync(int id);
        Task<List<Donation>> GetExpiredPendingAssignmentsAsync(DateTime asOf);
        Task<Ngo> GetNgoForDonationAsync(int donationId);
        Task SaveChangesAsync();
    }
}