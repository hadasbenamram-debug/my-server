using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoodRescue.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // ממשיך את שרשרת הבקשות בשרת
                await _next(context);
            }
            catch (Exception ex)
            {
                // רושם את השגיאה בלוגים של השרת
                _logger.LogError(ex, "שגיאה לא צפויה התרחשה במערכת: {Message}", ex.Message);

                // מחזיר תגובת שגיאה מסודרת ללקוח
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "אופס! ארעה שגיאה פנימית בשרת. אנא נסה שוב מאוחר יותר.",
                Detailed = exception.Message // מומלץ להסיר או להסתיר בסביבת Production משיקולי אבטחה
            };

            var jsonResponse = JsonSerializer.Serialize(response);
            return context.Response.WriteAsync(jsonResponse);
        }
    }
}