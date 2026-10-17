using AutoMapper;
using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MatchStatus = DAL.Classes.MatchStatus;

namespace BLL.Services
{
    public class FeedbackService : IFeedbackService
    {
        private readonly IFeedbackRepository _feedbackRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly VolunteerReliabilityService _reliability;
        private readonly CallerProfileService _caller;
        private readonly IMapper _mapper;

        public FeedbackService(
            IFeedbackRepository feedbackRepository,
            IMatchRepository matchRepository,
            VolunteerReliabilityService reliability,
            CallerProfileService caller,
            IMapper mapper)
        {
            _feedbackRepository = feedbackRepository;
            _matchRepository = matchRepository;
            _reliability = reliability;
            _caller = caller;
            _mapper = mapper;
        }

        public async Task<List<FeedbackResponseDto>> GetFeedbacksAsync(CurrentUserDto currentUser, PageRequest page)
        {
            var caller = await _caller.GetAsync(currentUser);

            if (caller.IsAdmin)
                return _mapper.Map<List<FeedbackResponseDto>>((await _feedbackRepository.GetPageAsync(page.Skip, page.PageSize)).Items);

            if (caller.Volunteer != null)
                return _mapper.Map<List<FeedbackResponseDto>>(await _feedbackRepository.GetForVolunteerAsync(caller.Volunteer.Id));

            // תוקן: בעבר נטענו כל המשובים במערכת לזיכרון וסוננו שם. עכשיו הסינון נעשה במסד.
            var feedbacks = await _feedbackRepository.GetForNgoOrSupplierAsync(caller.Ngo?.Id ?? -1, caller.Supplier?.Id ?? -1);
            return _mapper.Map<List<FeedbackResponseDto>>(feedbacks);
        }

        // יצירת משוב - רק העמותה של השידוך או הספק של התרומה, על שידוך שהושלם, ופעם אחת לכל שידוך.
        public async Task<FeedbackResponseDto> CreateAsync(CreateFeedbackDto dto, CurrentUserDto currentUser)
        {
            const string matchNotFound = "השידוך לא נמצא.";

            var match = await _matchRepository.GetByIdWithDonationAsync(dto.MatchId)
                        ?? throw new NotFoundException(matchNotFound);

            var caller = await _caller.GetAsync(currentUser);
            if (!caller.IsMatchNgo(match) && !caller.OwnsDonation(match.Donation))
                throw new NotFoundException(matchNotFound);

            if (match.Status != MatchStatus.Completed)
                throw new ArgumentException("ניתן להשאיר משוב רק על שידוך שהושלם.");

            if (await _feedbackRepository.ExistsForMatchAsync(match.Id))
                throw new ConflictException("כבר קיים משוב עבור שידוך זה.");

            var feedbackEntity = new Feedback
            {
                MatchId = match.Id,
                Rating = dto.Rating,
                VolunteerRating = dto.VolunteerRating,
                Comments = dto.Comments?.Trim() ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            await _feedbackRepository.AddAsync(feedbackEntity);

            // תוקן: הדירוג שהמתנדב קיבל נשמר ולא השפיע על כלום. עכשיו ציון האמינות שלו מחושב מחדש,
            // והמנוע משתמש בו בדירוג המתנדבים (ReliabilityWeight).
            await _reliability.RecalculateAsync(match.VolunteerId);

            var dtoOut = _mapper.Map<FeedbackResponseDto>(feedbackEntity);
            dtoOut.VolunteerId = match.VolunteerId;
            return dtoOut;
        }
    }
}
