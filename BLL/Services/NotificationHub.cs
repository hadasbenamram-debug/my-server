using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodRescue.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        // המחלקה יכולה להיות ריקה אם השימוש בה הוא רק לדחיפת התראות מהשרת ללקוחות,
        // או להכיל מתודות נוספות לפי הצורך.
    }
}