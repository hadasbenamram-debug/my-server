using System;
using System.Collections.Generic;

namespace FoodRescue.DTOs
{
    // התראה במרכז ההתראות (InApp). אותו מבנה נשלח גם בזמן אמת ב-SignalR ("ReceiveNotification").
    public class NotificationDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = "info";
        public string? Subject { get; set; }
        public string? Content { get; set; }
        public string? Link { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }

    public class MarkNotificationsReadDto
    {
        // null / ריק = לסמן הכל כנקרא
        public List<int>? Ids { get; set; }
    }
}
