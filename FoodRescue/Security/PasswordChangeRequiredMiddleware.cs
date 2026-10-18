using FoodRescue.Infrastructure;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace FoodRescue.Security
{
    // חדש: משתמש שנרשם וקיבל סיסמה זמנית במייל חייב להחליף אותה לפני שימוש במערכת.
    // עד ההחלפה מותרים רק: נתיבי ההזדהות (/api/auth/...) וצפייה/עדכון בפרופיל האישי.
    public sealed class PasswordChangeRequiredMiddleware
    {
        private readonly RequestDelegate _next;

        public PasswordChangeRequiredMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            if (context.User.HasClaim(SecurityStampValidator.PasswordChangeRequiredClaim, "true") && !IsAllowed(context.Request.Path))
            {
                return new ApiError
                {
                    Status = StatusCodes.Status403Forbidden,
                    Code = "PasswordChangeRequired",
                    Message = "יש להחליף את הסיסמה הזמנית לפני המשך השימוש במערכת.",
                    TraceId = context.TraceIdentifier
                }.WriteAsync(context);
            }

            return _next(context);
        }

        public static bool IsAllowed(PathString path) =>
            !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/users/me", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/notifications", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/region", StringComparison.OrdinalIgnoreCase);
    }
}
