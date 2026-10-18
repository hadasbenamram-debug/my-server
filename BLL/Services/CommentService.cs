using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class CommentService : ICommentService
    {
        private readonly ICommentRepository _commentRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IDonationRepository _donationRepository;
        private readonly IUserRepository _userRepository;
        private readonly CommentTreeService _commentTreeService;
        private readonly CallerProfileService _caller;

        public CommentService(
            ICommentRepository commentRepository,
            IMatchRepository matchRepository,
            IDonationRepository donationRepository,
            IUserRepository userRepository,
            CommentTreeService commentTreeService,
            CallerProfileService caller)
        {
            _commentRepository = commentRepository;
            _matchRepository = matchRepository;
            _donationRepository = donationRepository;
            _userRepository = userRepository;
            _commentTreeService = commentTreeService;
            _caller = caller;
        }

        public async Task<List<CommentDto>> GetTreeAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);

            var comments = caller.IsAdmin
                ? await _commentRepository.GetAllReadOnlyAsync()
                // -1 never matches a real Id, so a missing profile simply matches nothing.
                : await _commentRepository.GetForParticipantAsync(
                    caller.Volunteer?.Id ?? -1,
                    caller.Ngo?.Id ?? -1,
                    caller.Supplier?.Id ?? -1);

            return _commentTreeService.BuildCommentTree(comments);
        }

        public async Task<CommentDto> AddAsync(CreateCommentDto dto, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.UserId == null)
                throw new UnauthorizedException("המשתמש המחובר לא זוהה.");

            int? donationId = dto.DonationId;
            int? matchId = dto.MatchId;

            if (dto.ParentCommentId.HasValue)
            {
                var parent = await _commentRepository.GetByIdAsync(dto.ParentCommentId.Value)
                             ?? throw new ArgumentException("תגובת האב אינה תקינה.");

                // תגובה לתגובה שייכת תמיד לאותה תרומה / אותו שידוך של האב
                if ((donationId.HasValue && donationId != parent.DonationId) || (matchId.HasValue && matchId != parent.MatchId))
                    throw new ArgumentException("תגובת האב אינה תקינה.");

                donationId = parent.DonationId;
                matchId = parent.MatchId;
            }

            // בדיוק אחד מהשניים: תרומה או שידוך
            if (donationId.HasValue == matchId.HasValue)
                throw new ArgumentException("יש לציין תרומה או שידוך (בדיוק אחד מהם).");

            // אין הרשאה = אותה תשובה כמו "לא קיים", כדי לא לחשוף מזהים.
            if (!await CanCommentOnAsync(caller, donationId, matchId))
                throw new NotFoundException("היעד של התגובה לא נמצא.");

            var user = await _userRepository.GetByIdAsync(caller.UserId.Value)
                       ?? throw new UnauthorizedException("המשתמש המחובר לא נמצא.");

            var comment = new Comment
            {
                Id = 0,
                Author = Truncate(user.Name, 100),        // מהמשתמש המחובר, לא מהלקוח
                Content = dto.Content.Trim(),
                CreatedAt = DateTime.UtcNow,              // שעון השרת
                ParentCommentId = dto.ParentCommentId,
                DonationId = donationId,
                MatchId = matchId
            };

            await _commentRepository.AddAsync(comment);
            return CommentTreeService.ToDto(comment);
        }

        public async Task<List<CommentDto>> GetAllAsync()
        {
            var comments = await _commentRepository.GetAllReadOnlyAsync();
            return comments.Select(CommentTreeService.ToDto).ToList();
        }

        public async Task<int> DeleteAsync(int id)
        {
            var comment = await _commentRepository.GetByIdAsync(id)
                          ?? throw new NotFoundException("התגובה לא נמצאה.");

            var sameTarget = await _commentRepository.GetForTargetAsync(comment.DonationId, comment.MatchId);
            var toDelete = CommentTreeService.CollectSubtree(sameTarget, comment.Id);
            toDelete.Add(sameTarget.FirstOrDefault(c => c.Id == comment.Id) ?? comment);

            // מוחקים מהעלים כלפי מעלה (ה-FK ParentCommentId לא מוגדר Cascade)
            toDelete.Reverse();
            await _commentRepository.DeleteRangeAsync(toDelete);
            return toDelete.Count;
        }

        // Permission: admin, or the supplier who owns the donation, or the volunteer / NGO that is part of a match on it.
        private async Task<bool> CanCommentOnAsync(CallerProfile caller, int? donationId, int? matchId)
        {
            if (matchId.HasValue)
            {
                var match = await _matchRepository.GetByIdWithDonationAsync(matchId.Value);
                if (match == null) return false;

                return caller.IsAdmin ||
                       (caller.Volunteer != null && match.VolunteerId == caller.Volunteer.Id) ||
                       (caller.Ngo != null && match.NgoId == caller.Ngo.Id) ||
                       (caller.Supplier != null && match.Donation != null && match.Donation.SupplierId == caller.Supplier.Id);
            }

            int id = donationId!.Value;

            if (!await _donationRepository.ExistsAsync(id)) return false;
            if (caller.IsAdmin) return true;

            if (caller.Supplier != null && await _donationRepository.ExistsForSupplierAsync(id, caller.Supplier.Id))
                return true;

            return await _matchRepository.AnyForDonationAndParticipantAsync(id, caller.Volunteer?.Id ?? -1, caller.Ngo?.Id ?? -1);
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);
    }
}
