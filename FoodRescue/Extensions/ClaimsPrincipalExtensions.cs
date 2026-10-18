using FoodRescue.DTOs;
using System.Security.Claims;

namespace FoodRescue.Extensions
{
    // קריאת זהות המשתמש מתוך ה-JWT. זה חלק מטיפול בבקשת HTTP ולכן נשאר בשכבת ה-API;
    // הקונטרולר מעביר את התוצאה ל-Service כ-DTO.
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(value, out int userId) ? userId : null;
        }

        public static CurrentUserDto ToCurrentUser(this ClaimsPrincipal user) => new CurrentUserDto
        {
            UserId = user.GetUserId(),
            IsAdmin = user.IsInRole("Admin")
        };
    }
}
