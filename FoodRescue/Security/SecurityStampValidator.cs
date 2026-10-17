using BLL.Services;
using DAL.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FoodRescue.Security
{
    // בדיקה בכל בקשה מאומתת:
    // 1. ה-SecurityStamp בטוקן זהה לזה שבמסד. החלפת/איפוס סיסמה מחליפים את החותמת, ולכן כל הטוקנים הישנים מפסיקים לעבוד.
    //    (תוקן: בעבר טוקן ישן נשאר בתוקף עד סוף זמנו גם אחרי שינוי סיסמה.)
    // 2. התפקיד נלקח מהמסד ולא מהטוקן - כך שאישור/דחייה של מנהל משפיעים מיד, בלי לחכות לטוקן חדש.
    // 3. משתמש עם סיסמה זמנית מסומן בטענה שמגבילה אותו להחלפת סיסמה בלבד (PasswordChangeRequiredMiddleware).
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

            var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            var state = await users.GetAuthStateAsync(userId.Value);

            var tokenStamp = principal!.FindFirst(TokenService.SecurityStampClaimType)?.Value;
            if (state == null || string.IsNullOrEmpty(tokenStamp) || tokenStamp != state.SecurityStamp)
            {
                context.Fail("The token has been revoked.");
                return;
            }

            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToList())
                identity.RemoveClaim(roleClaim);
            identity.AddClaim(new Claim(identity.RoleClaimType, state.Role.ToString()));

            if (state.MustChangePassword)
                identity.AddClaim(new Claim(PasswordChangeRequiredClaim, "true"));
        }
    }
}
