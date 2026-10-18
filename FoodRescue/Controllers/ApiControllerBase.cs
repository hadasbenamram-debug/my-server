using BLL.Exceptions;
using DAL.Interfaces;
using FoodRescue.DTOs;
using FoodRescue.Security;
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

        protected CurrentUserDto CurrentUser => User.ToCurrentUser();

        // מזהה המשתמש מתוך ה-JWT. אם חסר - 401.
        protected int RequireUserId() =>
            User.GetUserId() ?? throw new UnauthorizedException("המשתמש אינו מחובר.");

        // רשימה מעומדת: הפריטים בגוף התשובה (מערך, כמו קודם), וסך הרשומות בכותרת X-Total-Count
        protected ActionResult<List<T>> Paged<T>(PagedResult<T> page)
        {
            Response.Headers[TotalCountHeader] = page.TotalCount.ToString(CultureInfo.InvariantCulture);
            return Ok(page.Items);
        }

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
