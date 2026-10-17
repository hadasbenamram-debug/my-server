using BLL.Notifications;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // חדש: מרכז ההתראות של המשתמש. התראות חדשות מגיעות גם בזמן אמת (SignalR: ReceiveNotification).
    [Authorize]
    [Route("api/[controller]")]
    public class NotificationsController : ApiControllerBase
    {
        private readonly IUserNotificationService _notifications;

        public NotificationsController(IUserNotificationService notifications)
        {
            _notifications = notifications;
        }

        [HttpGet]
        public async Task<ActionResult<List<NotificationDto>>> GetMine([FromQuery] PageRequest page) =>
            Paged(await _notifications.GetMineAsync(RequireUserId(), page));

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount() =>
            Ok(new { count = await _notifications.CountUnreadAsync(RequireUserId()) });

        // גוף ריק / בלי Ids = לסמן הכל כנקרא
        [HttpPost("mark-read")]
        public async Task<IActionResult> MarkRead(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] MarkNotificationsReadDto? dto)
        {
            await _notifications.MarkReadAsync(RequireUserId(), dto);
            return NoContent();
        }
    }
}
