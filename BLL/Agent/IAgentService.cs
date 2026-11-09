using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Agent
{
    // עדכון אחד בהזרמה: חלק מהתשובה (Text), או הודעת מצב בזמן שכלי רץ (Status, למשל "בודק את התרומות שלך...").
    // ה-BLL לא יודעת שיש HTTP: איך לשלוח את העדכונים לדפדפן (Server-Sent Events) מחליט ה-ChatController.
    public sealed record AgentStreamUpdate(string? Text, string? Status)
    {
        public static AgentStreamUpdate FromText(string text) => new(text, null);
        public static AgentStreamUpdate FromStatus(string status) => new(null, status);
    }

    public interface IAgentService
    {
        // שאלה ותשובה בבת אחת (בלי הזרמה) - לבדיקות
        Task<string> AskAsync(ChatRequestDto request, CurrentUserDto currentUser, CancellationToken ct);

        // התשובה בהזרמה, חלק אחרי חלק
        IAsyncEnumerable<AgentStreamUpdate> AskStreamingAsync(ChatRequestDto request, CurrentUserDto currentUser, CancellationToken ct);
    }
}
