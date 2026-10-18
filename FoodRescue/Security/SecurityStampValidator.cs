using BLL.Interfaces;
using BLL.Services;
using FoodRescue.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FoodRescue.Security
{
    // בדיקה בכל בקשה מאומתת:
    // 1. ה-SecurityStamp בטוקן זהה לזה שבמסד. החלפת/איפוס סיסמה מחליפים את החותמת, ולכן כל הטוקנים הישנים מפסיקים לעבוד.
    // 2. התפקיד נלקח מהמסד ולא מהטוקן - כך שאישור/דחייה של מנהל משפיעים מיד, בלי לחכות לטוקן חדש.
    // 3. משתמש עם סיסמה זמנית מסומן בטענה שמגבילה אותו להחלפת סיסמה בלבד (PasswordChangeRequiredMiddleware).
    // הבדיקה עצמה נעשית ב-ISessionValidationService (BLL). כאן רק קוראים את הטוקן ומעדכנים את ה-claims.
    public static class SecurityStampValidator
    {
        public const string PasswordChangeRequiredClaim = "pwd_change_required";

        public static async Task ValidateAsync(TokenValidatedContext context)
        {
            var principal = context.Principal;
            var identity = principal?.Identity as ClaimsIdentity;
            var userId = principal?.GetUserId();

            if (identity == null || userId == null)
            {
                context.Fail("Invalid token.");
                return;
            }

            var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionValidationService>();
            var tokenStamp = principal!.FindFirst(TokenService.SecurityStampClaimType)?.Value;
            var state = await sessions.ValidateAsync(userId.Value, tokenStamp);

            if (state == null)
            {
                context.Fail("The token has been revoked.");
                return;
            }

            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToList())
                identity.RemoveClaim(roleClaim);
            identity.AddClaim(new Claim(identity.RoleClaimType, state.Role));

            if (state.MustChangePassword)
                identity.AddClaim(new Claim(PasswordChangeRequiredClaim, "true"));
        }
    }
}
