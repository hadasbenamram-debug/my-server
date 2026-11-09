using BLL.Agent;
using FoodRescue.DTOs;
using FoodRescue.Middlewares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // העוזר החכם (AI Agent). רק למשתמש מחובר: הכלים עונים על "התרומות שלי", "המסלול שלי" וכו',
    // והמשתמש נקבע מה-JWT (CurrentUser) - לא ממה שנכתב בצ'אט.
    // הגבלת קצב: כל שאלה יכולה לגרור כמה קריאות למודל (ראו "agent-chat" ב-Program.cs).
    [Authorize]
    [Route("api/[controller]")]
    [EnableRateLimiting("agent-chat")]
    public class ChatController : ApiControllerBase
    {
        private readonly IAgentService _agentService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IAgentService agentService, ILogger<ChatController> logger)
        {
            _agentService = agentService;
            _logger = logger;
        }

        // שאלה ותשובה בבת אחת, בלי הזרמה - לבדיקות (FoodRescue.http / Postman). הקליינט משתמש ב-stream.
        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequestDto dto, CancellationToken ct)
        {
            RequireUserId();
            var answer = await _agentService.AskAsync(dto, CurrentUser, ct);
            return Ok(new { text = answer });
        }

        // התשובה בהזרמה (Server-Sent Events): כל הודעה היא שורת "data: {json}" ואחריה שורה ריקה.
        //   data: {"text":"..."}                 - חלק מהתשובה
        //   event: status / data: {"text":"..."} - הודעת מצב בזמן שכלי רץ ("בודק את התרומות שלך...")
        //   event: error  / data: {"message":"..."} - שגיאה אחרי שההזרמה כבר התחילה
        //   data: [DONE]                         - סוף התשובה
        // למה זה כאן ולא ב-Service? כי זה קוד של HTTP. ה-AgentService רק מחזיר עדכונים ב-IAsyncEnumerable.
        [HttpPost("stream")]
        public async Task Stream([FromBody] ChatRequestDto dto, CancellationToken ct)
        {
            RequireUserId();

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";   // שפרוקסי (למשל nginx) לא יחזיק את החלקים אצלו

            try
            {
                await foreach (var update in _agentService.AskStreamingAsync(dto, CurrentUser, ct))
                {
                    if (update.Status != null)
                        await WriteEventAsync("status", new { text = update.Status }, ct);
                    else if (!string.IsNullOrEmpty(update.Text))
                        await WriteEventAsync(null, new { text = update.Text }, ct);
                }

                await Response.WriteAsync("data: [DONE]\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (Exception ex) when (Response.HasStarted && ex is not OperationCanceledException)
            {
                // התשובה כבר התחילה (קוד 200 כבר יצא לדפדפן), ולכן ה-Middleware כבר לא יכול לשנות אותה.
                // שגיאה לפני החלק הראשון (למשל בקשה לא תקינה או מפתח API חסר) ממשיכה ל-ExceptionHandlingMiddleware כרגיל.
                _logger.LogWarning(ex, "The assistant stream was interrupted.");

                // אותו תרגום שגיאה -> הודעה כמו בכל ה-API (למשל מכסת ה-AI נגמרה)
                var error = ExceptionHandlingMiddleware.Map(ex, HttpContext.TraceIdentifier);
                var message = error.Status >= 500 && error.Code == "ServerError"
                    ? "התשובה נקטעה באמצע. נסו לשאול שוב."
                    : error.Message;

                await WriteEventAsync("error", new { message, code = error.Code }, CancellationToken.None);
            }
            // ביטול של המשתמש (OperationCanceledException) הוא לא שגיאה: אין למי לשלוח הודעה.
        }

        private async Task WriteEventAsync(string? eventName, object payload, CancellationToken ct)
        {
            // JSON - כדי ששורה חדשה בתוך הטקסט לא תשבור את הפורמט
            var json = JsonSerializer.Serialize(payload);
            var block = eventName == null ? $"data: {json}\n\n" : $"event: {eventName}\ndata: {json}\n\n";

            await Response.WriteAsync(block, ct);
            await Response.Body.FlushAsync(ct);   // לשלוח עכשיו, לא לחכות בזיכרון
        }
    }
}
