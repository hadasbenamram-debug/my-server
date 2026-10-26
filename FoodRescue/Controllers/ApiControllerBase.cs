using BLL.Exceptions;
using BLL.Interfaces;
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

        // תיקון 10: תשובה להוספת תפקיד (מתנדב / ספק / עמותה) לחשבון של המשתמש המחובר - בלי חריגות במקרים הצפויים
        protected IActionResult AddRoleResponse(AddRoleResult result, string roleTitle) => result switch
        {
            AddRoleResult.Added => Message(
                $"הבקשה להצטרף גם {roleTitle} נשלחה לאישור מנהל. אחרי האישור יישלח אליך מייל, ואפשר יהיה לעבור בין התפקידים מהתפריט."),
            AddRoleResult.AlreadyHasRole => Conflict(new Infrastructure.ApiError
            {
                Status = 409, Code = "AlreadyHasRole",
                Message = $"החשבון שלך כבר רשום {roleTitle} (או שהבקשה עדיין ממתינה לאישור)."
            }),
            AddRoleResult.NotAvailableForAdmin => StatusCode(403, new Infrastructure.ApiError
            {
                Status = 403, Code = "NotAvailableForAdmin",
                Message = "לחשבון מנהל לא מוסיפים תפקידים. כדי להתנדב או לתרום - נרשמים עם כתובת מייל אחרת."
            }),
            _ => Unauthorized()
        };
    }

    public class MessageResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
