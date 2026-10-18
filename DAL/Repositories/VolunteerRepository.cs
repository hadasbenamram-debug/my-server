using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class VolunteerRepository : Repository<Volunteer>, IVolunteerRepository
    {
        public VolunteerRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<Volunteer>> GetAvailableVolunteersAsync(DateTime todayUtc)
        {
            var today = todayUtc.Date;

            // תוקן: בעבר נבדק רק IsAvailable, כך שמתנדב שעדיין לא אושר (או נדחה) יכול היה לקבל שידוך.
            // רישיון שתוקפו פג מוציא את המתנדב מהשיבוץ (רישיון בלי תאריך תוקף - רשומות ישנות - לא נחסם).
            return await _context.Volunteers
                .Where(v => v.IsAvailable
                         && v.ApprovalStatus == ApprovalStatus.Approved
                         && (v.DriverLicenseExpiry == null || v.DriverLicenseExpiry >= today))
                .Include(v => v.AvailabilitySlots)
                .Include(v => v.User)
                .ToListAsync();
        }

        public async Task<Volunteer?> GetByUserIdAsync(int userId)
        {
            return await _context.Volunteers
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.UserId == userId);
        }

        public async Task<Volunteer?> GetOwnedAsync(int id, int userId)
        {
            return await _context.Volunteers
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
        }

        public async Task<List<Volunteer>> GetAllWithUserAsync()
        {
            return await _context.Volunteers
                .Include(v => v.User)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Volunteer>> GetByApprovalStatusWithUserAsync(ApprovalStatus status, int? take = null)
        {
            IQueryable<Volunteer> query = _context.Volunteers
                .Include(v => v.User)
                .Include(v => v.AvailabilitySlots)
                .Where(v => v.ApprovalStatus == status)
                .OrderByDescending(v => v.ReliabilityScore);

            if (take.HasValue)
                query = query.Take(take.Value);

            return await query.AsNoTracking().ToListAsync();
        }

        public async Task<int> CountByApprovalStatusAsync(ApprovalStatus status)
        {
            return await _context.Volunteers.CountAsync(v => v.ApprovalStatus == status);
        }

        public async Task<int> CountAvailableAsync()
        {
            return await _context.Volunteers.CountAsync(v => v.IsAvailable && v.ApprovalStatus == ApprovalStatus.Approved);
        }
    }
}
