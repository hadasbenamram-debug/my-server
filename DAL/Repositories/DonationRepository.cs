using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class DonationRepository : Repository<Donation>, IDonationRepository
    {
        public DonationRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<Donation>> GetAvailableDonationsAsync(DateTime nowUtc, DateTime localDayStartUtc)
        {
            return await _context.Donations
                .Include(d => d.DonationFoodCategories)
                    .ThenInclude(dfc => dfc.Category)
                .Include(d => d.Region)
                .Where(d => d.Status == DonationStatus.Pending
                         && d.ExpirationTime > nowUtc
                         && d.CreatedAt >= localDayStartUtc
                         && d.Supplier.ApprovalStatus == ApprovalStatus.Approved)
                .ToListAsync();
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        public async Task<List<Donation>> GetAllWithCategoriesAsync()
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
                .Include(d => d.Supplier)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<Donation?> GetForUpdateAsync(int id)
        {
            return await _context.Donations
                .Include(d => d.DonationFoodCategories)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<Donation?> GetFreshAsync(int id)
        {
            var donation = await _context.Donations.FindAsync(id);
            if (donation != null)
                await _context.Entry(donation).ReloadAsync();
            return donation;
        }

        public async Task<List<Donation>> GetExpiredOpenAsync(DateTime nowUtc)
        {
            return await _context.Donations
                .Where(d => (d.Status == DonationStatus.Pending || d.Status == DonationStatus.Matched)
                         && d.ExpirationTime <= nowUtc)
                .ToListAsync();
        }

        public async Task<List<Donation>> GetBySupplierIdWithRegionAsync(int supplierId)
        {
            return await _context.Donations
                .Include(d => d.Region)
                .Where(d => d.SupplierId == supplierId)
                .OrderByDescending(d => d.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<PagedResult<Donation>> GetPageWithSupplierAsync(int skip, int take)
        {
            var query = _context.Donations
                .Include(d => d.Supplier)
                .OrderByDescending(d => d.CreatedAt)
                .AsNoTracking();
            return PageAsync(query, skip, take);
        }

        public async Task<List<string>> GetImageFilePathsAsync()
        {
            return await _context.Donations
                .Where(d => d.ImageFilePath != null && d.ImageFilePath != "")
                .Select(d => d.ImageFilePath!)
                .ToListAsync();
        }

        public async Task<bool> ExistsAsync(int id)
        {
            return await _context.Donations.AnyAsync(d => d.Id == id);
        }

        public async Task<bool> ExistsForSupplierAsync(int donationId, int supplierId)
        {
            return await _context.Donations.AnyAsync(d => d.Id == donationId && d.SupplierId == supplierId);
        }

        public async Task<bool> AnyInRegionAsync(int regionId)
        {
            return await _context.Donations.AnyAsync(d => d.RegionId == regionId);
        }

        public async Task<int> CountByStatusAsync(DonationStatus status)
        {
            return await _context.Donations.CountAsync(d => d.Status == status);
        }

        public async Task<bool> TryUpdateAsync(Donation donation)
        {
            try
            {
                await UpdateAsync(donation);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }
    }
}
