using BLL.Exceptions;
using FoodRescue.DTOs;
using FoodRescue.Extensions;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Globalization;

namespace FoodRescue.Controllers
{
    // בסיס משותף לקונטרולרים. אין כאן try/catch: חריגות עסקיות מתורגמות לתשובה ב-ExceptionHandlingMiddleware.
    [ApiController]
    public abstract class ApiControllerBase : ControllerBase
    {
        public const string TotalCountHeader = "X-Total-Count";

        // תשובה אחידה לכל טפסי ההרשמה - גם כשהמייל חדש וגם כשהוא כבר רשום, כדי לא לחשוף אילו מיילים קיימים.
        // הפרטים (סיסמה זמנית / "כבר יש לך חשבון") נשלחים רק לתיבת המייל עצמה.
        public const string RegistrationAcceptedMessage =
            "תודה! הבקשה נקלטה, ושלחנו לכתובת המייל שהזנת הודעה עם המשך התהליך. אם המייל לא הגיע תוך כמה דקות - כדאי לבדוק גם בתיקיית הספאם.";

        protected CurrentUserDto CurrentUser => User.ToCurrentUser();

        // מזהה המשתמש מתוך ה-JWT. אם חסר - 401.
        protected int RequireUserId() =>
            User.GetUserId() ?? throw new UnauthorizedException("המשתמש אינו מחובר.");

        // רשימה מעומדת: הפריטים בגוף התשובה (מערך, כמו קודם), וסך הרשומות בכותרת X-Total-Count
        protected ActionResult<List<T>> Paged<T>(PagedResponse<T> page)
        {
            Response.Headers[TotalCountHeader] = page.TotalCount.ToString(CultureInfo.InvariantCulture);
            return Ok(page.Items);
        }

        protected OkObjectResult Message(string message) => Ok(new MessageResponse { Message = message });
    }

    public class MessageResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
