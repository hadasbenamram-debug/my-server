using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoodRescue.Infrastructure
{
    // מבנה שגיאה אחיד לכל ה-API (בעבר היו 5 צורות שונות: {message}, מחרוזת, ProblemDetails, {success,message}, {StatusCode,Message,Detailed}).
    // { "status": 404, "message": "...", "code": "NotFound", "errors": { "field": ["..."] } }
    public sealed class ApiError
    {
        public int Status { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Code { get; init; }
        public Dictionary<string, string[]>? Errors { get; init; }
        public string? TraceId { get; init; }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static ApiError FromModelState(ModelStateDictionary modelState, string? traceId)
        {
            var errors = modelState
                .Where(e => e.Value != null && e.Value.Errors.Count > 0)
                .ToDictionary(
                    e => string.IsNullOrEmpty(e.Key) ? "body" : JsonNamingPolicy.CamelCase.ConvertName(e.Key),
                    e => e.Value!.Errors
                        .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? "ערך לא תקין." : x.ErrorMessage)
                        .ToArray());

            var first = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "הנתונים שנשלחו אינם תקינים.";

            return new ApiError
            {
                Status = StatusCodes.Status400BadRequest,
                Message = first,
                Code = "ValidationFailed",
                Errors = errors,
                TraceId = traceId
            };
        }

        public Task WriteAsync(HttpContext context)
        {
            context.Response.StatusCode = Status;
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync(JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
