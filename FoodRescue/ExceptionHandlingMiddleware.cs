using BLL.Exceptions;
using FoodRescue.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FoodRescue.Middlewares
{
    // טיפול מרכזי בשגיאות: הקונטרולרים לא עוטפים יותר כל פעולה ב-try/catch.
    // השירותים זורקים חריגות עסקיות, וכאן הן מתורגמות לקוד HTTP ולמבנה שגיאה אחיד (ApiError).
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // הלקוח ניתק - אין למי להחזיר תשובה
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = 499;
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted)
                {
                    _logger.LogError(ex, "Unhandled exception after the response started.");
                    throw;
                }

                var error = Map(ex, Activity.Current?.Id ?? context.TraceIdentifier, _environment.IsDevelopment());

                if (error.Status >= 500)
                    _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
                else
                    _logger.LogInformation("Request {Method} {Path} failed with {Status}: {Message}",
                        context.Request.Method, context.Request.Path, error.Status, error.Message);

                context.Response.Clear();
                await error.WriteAsync(context);
            }
        }

        // ציבורי כדי שאפשר יהיה לבדוק את המיפוי בטסט
        public static ApiError Map(Exception ex, string? traceId, bool includeDetails = false) => ex switch
        {
            NotFoundException e => Error(StatusCodes.Status404NotFound, "NotFound", e.Message, traceId),
            UnauthorizedException e => Error(StatusCodes.Status401Unauthorized, "Unauthorized", e.Message, traceId),
            ForbiddenException e => Error(StatusCodes.Status403Forbidden, "Forbidden", e.Message, traceId),
            ConflictException e => Error(StatusCodes.Status409Conflict, "Conflict", e.Message, traceId),
            ServiceUnavailableException e => Error(StatusCodes.Status503ServiceUnavailable, "ServiceUnavailable", e.Message, traceId),
            DbUpdateConcurrencyException => Error(StatusCodes.Status409Conflict, "Conflict",
                "הנתונים שונו בינתיים על ידי פעולה אחרת. יש לרענן ולנסות שוב.", traceId),
            BadHttpRequestException e => Error(e.StatusCode, "BadRequest", "הבקשה אינה תקינה.", traceId),
            // ArgumentNullException / ArgumentOutOfRangeException יורשות מ-ArgumentException
            ArgumentException e => Error(StatusCodes.Status400BadRequest, "BadRequest", CleanArgumentMessage(e), traceId),
            IOException => Error(StatusCodes.Status500InternalServerError, "StorageError",
                "שמירת הקובץ נכשלה. נסו שוב מאוחר יותר.", traceId),
            _ => Error(StatusCodes.Status500InternalServerError, "ServerError",
                includeDetails ? $"שגיאה פנימית בשרת: {ex.Message}" : "אופס! ארעה שגיאה פנימית בשרת. אנא נסו שוב מאוחר יותר.",
                traceId)
        };

        private static ApiError Error(int status, string code, string message, string? traceId) =>
            new() { Status = status, Code = code, Message = message, TraceId = traceId };

        // ArgumentException מוסיף " (Parameter 'x')" להודעה - לא מציגים את זה למשתמש
        private static string CleanArgumentMessage(ArgumentException e)
        {
            var message = e.Message;
            if (e.ParamName != null)
            {
                var suffix = $" (Parameter '{e.ParamName}')";
                if (message.EndsWith(suffix, StringComparison.Ordinal))
                    message = message.Substring(0, message.Length - suffix.Length);
            }
            return string.IsNullOrWhiteSpace(message) ? "הבקשה אינה תקינה." : message;
        }
    }
}
