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
            return await _context.Comments.OrderBy(c => c.CreatedAt).AsNoTracking().ToListAsync();
        }

        public async Task<List<Comment>> GetForParticipantAsync(int volunteerId, int ngoId, int supplierId)
        {
            return await _context.Comments
                .AsNoTracking()
                .Where(c =>
                    (c.DonationId != null &&
                        (c.Donation.SupplierId == supplierId ||
                         c.Donation.Matches.Any(m => m.VolunteerId == volunteerId || m.NgoId == ngoId))) ||
                    (c.MatchId != null &&
                        (c.Match.VolunteerId == volunteerId ||
                         c.Match.NgoId == ngoId ||
                         c.Match.Donation.SupplierId == supplierId)))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Comment>> GetForTargetAsync(int? donationId, int? matchId)
        {
            return await _context.Comments
                .Where(c => c.DonationId == donationId && c.MatchId == matchId)
                .ToListAsync();
        }

        public async Task DeleteRangeAsync(IEnumerable<Comment> comments)
        {
            _context.Comments.RemoveRange(comments);
            await _context.SaveChangesAsync();
        }
    }
}
