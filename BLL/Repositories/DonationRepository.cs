using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BLL.Interfaces;

namespace BLL.Repositories
{
    public class DonationRepository : Repository<Donation>, IDonationRepository
    {
        public DonationRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<Donation>> GetExpiredPendingAssignmentsAsync(DateTime asOf)
        {
            return await _context.Donations
                .Where(d => d.Status == DonationStatus.Pending && d.ExpirationTime < asOf)
                .ToListAsync();
        }

        public async Task<Ngo> GetNgoForDonationAsync(int donationId)
        {
            var donation = await _context.Donations
                .Include(d => d.Region)
                .FirstOrDefaultAsync(d => d.Id == donationId);

            if (donation == null) return null;

            // שליפת ה-NGO המשויך לאזור (Region) של התרומה
            return await _context.Ngos
                .FirstOrDefaultAsync(n => n.RegionId == donation.RegionId);
        }

        public async Task<IEnumerable<Donation>> GetAvailableDonationsAsync()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            return await _context.Donations
                .Include(d => d.DonationFoodCategories)
                    .ThenInclude(dfc => dfc.Category)
                .Include(d => d.Region)
                .Where(d => d.Status == DonationStatus.Pending
                         && d.ExpirationTime > now
                         && d.CreatedAt.Date == today)
                .ToListAsync();
        }
        public Task SaveChangesAsync() => _context.SaveChangesAsync();
        public async Task<IEnumerable<Donation>> GetAllWithCategoriesAsync()
        {
            return await _context.Donations
                .Include(d => d.DonationFoodCategories)
                    .ThenInclude(dfc => dfc.Category)
                .Include(d => d.Region)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Donation?> GetByIdWithCategoriesAsync(int id)
        {
            return await _context.Donations
                .Include(d => d.DonationFoodCategories)
                    .ThenInclude(dfc => dfc.Category)
                .Include(d => d.Region)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);
        }
    }
}