using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IFeedbackService
    {
        // מנהל רואה הכל, מתנדב את מה שנוגע אליו, עמותה/ספק את מה ששייך אליהם
        Task<List<FeedbackResponseDto>> GetFeedbacksAsync(CurrentUserDto currentUser, PageRequest page);

        Task<FeedbackResponseDto> CreateAsync(CreateFeedbackDto dto, CurrentUserDto currentUser);
    }
}
