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
using RequestedRole = FoodRescue.DTOs.RequestedRole;

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

        // תיקון 18: role - למשתמש עם כמה תפקידים, דף התגובות של כל תפקיד מציג רק את התרומות של התפקיד הזה
        // (למשל ספק שהוא גם מתנדב: בדף של המתנדב - התרומות שהוא אוסף, לא התרומות שהוא פרסם). בלי role - הכל, כמו קודם.
        public async Task<List<CommentDto>> GetTreeAsync(CurrentUserDto currentUser, RequestedRole? role = null)
        {
            var caller = await _caller.GetAsync(currentUser);

            bool any = role == null;
            var comments = caller.IsAdmin
                ? await _commentRepository.GetAllReadOnlyAsync()
                // -1 never matches a real Id, so a missing profile simply matches nothing.
                : await _commentRepository.GetForParticipantAsync(
                    any || role == RequestedRole.Volunteer ? caller.Volunteer?.Id ?? -1 : -1,
                    any || role == RequestedRole.Ngo ? caller.Ngo?.Id ?? -1 : -1,
                    any || role == RequestedRole.Supplier ? caller.Supplier?.Id ?? -1 : -1);

            return _commentTreeService.BuildCommentTree(comments);
        }

        // תיקון 18: כל תגובה היא על תרומה מסוימת - DonationId חובה (בתשובה לתגובה: התרומה של התגובה שאליה עונים).
        // אין יותר תגובות על שיבוץ (MatchId): מתנדב מגיב על התרומה שהוא אוסף.
        public async Task<CommentDto> AddAsync(CreateCommentDto dto, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.UserId == null)
                throw new UnauthorizedException("המשתמש המחובר לא זוהה.");

            int donationId;

            if (dto.ParentCommentId.HasValue)
            {
                var parent = await _commentRepository.GetByIdAsync(dto.ParentCommentId.Value)
                             ?? throw new ArgumentException("תגובת האב אינה תקינה.");

                // תשובה לתגובה שייכת תמיד לאותה תרומה של האב
                if (dto.DonationId.HasValue && dto.DonationId != parent.DonationId)
                    throw new ArgumentException("תשובה לתגובה חייבת להיות על אותה תרומה.");

                donationId = parent.DonationId;
            }
            else
            {
                donationId = dto.DonationId ?? throw new ArgumentException("יש לבחור את התרומה שעליה התגובה.");
            }

            // אין הרשאה = אותה תשובה כמו "לא קיים", כדי לא לחשוף מזהים.
            if (!await CanCommentOnAsync(caller, donationId))
                throw new NotFoundException("התרומה של התגובה לא נמצאה.");

            var user = await _userRepository.GetByIdAsync(caller.UserId.Value)
                       ?? throw new UnauthorizedException("המשתמש המחובר לא נמצא.");

            var comment = new Comment
            {
                Id = 0,
                Author = Truncate(user.Name, 100),        // מהמשתמש המחובר, לא מהלקוח
                Content = dto.Content.Trim(),
                CreatedAt = DateTime.UtcNow,              // שעון השרת
                ParentCommentId = dto.ParentCommentId,
                DonationId = donationId
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

            var sameDonation = await _commentRepository.GetForDonationAsync(comment.DonationId);
            var toDelete = CommentTreeService.CollectSubtree(sameDonation, comment.Id);
            toDelete.Add(sameDonation.FirstOrDefault(c => c.Id == comment.Id) ?? comment);

            // מוחקים מהעלים כלפי מעלה (ה-FK ParentCommentId לא מוגדר Cascade)
            toDelete.Reverse();
            await _commentRepository.DeleteRangeAsync(toDelete);
            return toDelete.Count;
        }

        // Permission: admin, or the supplier who owns the donation, or the volunteer / NGO that is part of a match on it.
        private async Task<bool> CanCommentOnAsync(CallerProfile caller, int id)
        {
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
