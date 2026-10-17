using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Notifications
{
    // מרכז ההתראות של המשתמש המחובר (קריאה, ספירת לא-נקראו, סימון כנקרא)
    public interface IUserNotificationService
    {
        Task<PagedResponse<NotificationDto>> GetMineAsync(int userId, PageRequest page);
        Task<int> CountUnreadAsync(int userId);
        Task MarkReadAsync(int userId, MarkNotificationsReadDto? dto);
    }

    public sealed class UserNotificationService : IUserNotificationService
    {
        private readonly INotificationRepository _repository;

        public UserNotificationService(INotificationRepository repository)
        {
            _repository = repository;
        }

        public async Task<PagedResponse<NotificationDto>> GetMineAsync(int userId, PageRequest page)
        {
            var result = await _repository.GetInAppForUserAsync(userId, page.Skip, page.PageSize);
            return new PagedResponse<NotificationDto>
            {
                Items = result.Items.Select(n => NotificationService.ToDto(n)).ToList(),
                TotalCount = result.TotalCount
            };
        }

        public Task<int> CountUnreadAsync(int userId) => _repository.CountUnreadInAppAsync(userId);

        public Task MarkReadAsync(int userId, MarkNotificationsReadDto? dto)
        {
            var ids = dto?.Ids is { Count: > 0 } list ? list.Distinct().ToList() : null;
            return _repository.MarkInAppReadAsync(userId, ids, DateTime.UtcNow);
        }
    }
}
