using DAL.Classes;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repositories
{
    public class CommentRepository : Repository<Comment>, ICommentRepository
    {
        public CommentRepository(FoodRescueDbContext context) : base(context) { }

        public async Task<List<Comment>> GetAllReadOnlyAsync()
        {
            return await _context.Comments
                .Include(c => c.Donation).ThenInclude(d => d.Supplier)
                .OrderBy(c => c.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // תיקון 18: כל תגובה שייכת לתרומה. רואים אותה הספק של התרומה, וכל מתנדב / עמותה שהיה להם שיבוץ על התרומה
        // (כמו הכלל שהיה קודם לתגובות על תרומה; התגובות שהיו על שיבוץ הועברו לתרומה של השיבוץ).
        public async Task<List<Comment>> GetForParticipantAsync(int volunteerId, int ngoId, int supplierId)
        {
            return await _context.Comments
                .Include(c => c.Donation).ThenInclude(d => d.Supplier)
                .AsNoTracking()
                .Where(c =>
                    c.Donation.SupplierId == supplierId ||
                    c.Donation.Matches.Any(m => m.VolunteerId == volunteerId || m.NgoId == ngoId))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Comment>> GetForDonationAsync(int donationId)
        {
            return await _context.Comments
                .Where(c => c.DonationId == donationId)
                .ToListAsync();
        }

        public async Task DeleteRangeAsync(IEnumerable<Comment> comments)
        {
            _context.Comments.RemoveRange(comments);
            await _context.SaveChangesAsync();
        }
    }
}
