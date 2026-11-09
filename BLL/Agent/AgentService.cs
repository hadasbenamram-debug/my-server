using BLL.Algorithms;
using BLL.Configuration;
using FoodRescue.DTOs;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Agent
{
    // העוזר החכם: מוסיף את הודעת המערכת ואת הכלים של התפקיד, וקורא למודל.
    // את הלולאה "המודל מבקש כלי -> מריצים -> שולחים לו את התוצאה -> שואלים שוב" מריץ
    // UseFunctionInvocation שנרשם ב-Program.cs. כאן רק בוחרים אילו כלים המודל מקבל.
    public sealed class AgentService : IAgentService
    {
        private readonly IChatClient _chatClient;
        private readonly AgentCaller _caller;
        private readonly DonationTools _donationTools;
        private readonly NgoTools _ngoTools;
        private readonly VolunteerTools _volunteerTools;
        private readonly AdminTools _adminTools;
        private readonly CalendarTools _calendarTools;
        private readonly IMatchingPolicyProvider _policy;
        private readonly TimeProvider _clock;

        public AgentService(
            IChatClient chatClient,
            AgentCaller caller,
            DonationTools donationTools,
            NgoTools ngoTools,
            VolunteerTools volunteerTools,
            AdminTools adminTools,
            CalendarTools calendarTools,
            IMatchingPolicyProvider policy,
            TimeProvider clock)
        {
            _chatClient = chatClient;
            _caller = caller;
            _donationTools = donationTools;
            _ngoTools = ngoTools;
            _volunteerTools = volunteerTools;
            _adminTools = adminTools;
            _calendarTools = calendarTools;
            _policy = policy;
            _clock = clock;
        }

        public async Task<string> AskAsync(ChatRequestDto request, CurrentUserDto currentUser, CancellationToken ct)
        {
            var conversation = await PrepareAsync(request, currentUser);
            var response = await _chatClient.GetResponseAsync(conversation.Messages, conversation.Options, ct);
            return response.Text;
        }

        public async IAsyncEnumerable<AgentStreamUpdate> AskStreamingAsync(
            ChatRequestDto request,
            CurrentUserDto currentUser,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var conversation = await PrepareAsync(request, currentUser);
            var updates = _chatClient.GetStreamingResponseAsync(conversation.Messages, conversation.Options, ct);

            await foreach (var update in updates)
            {
                // המודל ביקש כלי: בזמן שהוא רץ אין טקסט להזרים, אז מודיעים למשתמש מה בודקים עכשיו
                foreach (var call in update.Contents.OfType<FunctionCallContent>())
                    yield return AgentStreamUpdate.FromStatus(conversation.Toolbox.StatusFor(call.Name));

                // חלק מהעדכונים הם קריאות לכלים או תוצאות שלהם, בלי טקסט
                if (!string.IsNullOrEmpty(update.Text))
                    yield return AgentStreamUpdate.FromText(update.Text);
            }
        }

        // ---------------------------------------------------------------

        private sealed record Conversation(List<ChatMessage> Messages, ChatOptions Options, AgentToolbox Toolbox);

        private async Task<Conversation> PrepareAsync(ChatRequestDto request, CurrentUserDto currentUser)
        {
            if (request?.Messages == null || request.Messages.Count == 0 || request.Messages[^1].Role != "user")
                throw new ArgumentException("ההודעה האחרונה בשיחה חייבת להיות שאלה של המשתמש.");

            // מי שואל ובאיזה תפקיד - מה-JWT ומהמסד, לא ממה שהמודל או הקליינט אומרים
            await _caller.InitializeAsync(currentUser, request.ActiveRole);

            var toolbox = BuildToolbox(_caller.Role);
            var nowLocal = LocalClock.ToLocal(_clock.GetUtcNow().UtcDateTime);

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, AgentPrompt.Build(_caller.Role, _policy.Current, nowLocal))
            };

            foreach (var m in request.Messages)
            {
                var role = m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
                messages.Add(new ChatMessage(role, m.Text));
            }

            var options = new ChatOptions
            {
                Tools = toolbox.Tools,
                // תשובות שמבוססות על נתונים: פחות "יצירתיות", פחות המצאות
                Temperature = 0.3f
            };

            return new Conversation(messages, options, toolbox);
        }

        // כל תפקיד מקבל רק את הכלים שלו. המודל יכול לבקש רק כלים שנתנו לו - ולכן ספק לא יכול לבקש
        // את המסלול של מתנדב, ומתנדב לא יכול לבקש את תמונת המצב של המנהל.
        // כל הכלים קוראים מידע בלבד: אין כלי שיוצר, משנה, מאשר או מבטל.
        internal AgentToolbox BuildToolbox(AgentRole role)
        {
            var toolbox = new AgentToolbox();

            switch (role)
            {
                case AgentRole.Supplier:
                    toolbox.Add(_donationTools.GetMyDonationsAsync, "בודק את התרומות שלך...");
                    toolbox.Add(_donationTools.GetDonationDetailsAsync, "בודק את פרטי התרומה...");
                    break;

                case AgentRole.Ngo:
                    toolbox.Add(_ngoTools.GetIncomingDonationsAsync, "בודק אילו תרומות בדרך אליכם...");
                    toolbox.Add(_ngoTools.GetMyReceivingHoursAsync, "בודק את שעות הקבלה שלכם...");
                    toolbox.Add(_donationTools.GetDonationDetailsAsync, "בודק את פרטי התרומה...");
                    break;

                case AgentRole.Volunteer:
                    toolbox.Add(_volunteerTools.GetMyActiveRouteAsync, "בודק את המסלול שלך...");
                    toolbox.Add(_volunteerTools.GetMyAvailabilityAsync, "בודק את הזמינות השבועית שלך...");
                    toolbox.Add(_volunteerTools.GetMyDeliveryHistoryAsync, "בודק את היסטוריית המשלוחים שלך...");
                    break;

                case AgentRole.Admin:
                    toolbox.Add(_adminTools.GetSystemOverviewAsync, "בודק את מצב המערכת ומנוע השידוכים...");
                    toolbox.Add(_adminTools.GetLiveMissionsAsync, "בודק אילו מסלולים בדרך עכשיו...");
                    toolbox.Add(_donationTools.GetDonationDetailsAsync, "בודק את פרטי התרומה...");
                    break;
            }

            // לכל התפקידים: שבת (חישוב פנימי) וחגים (Hebcal - API חיצוני)
            toolbox.Add(_calendarTools.GetShabbatTimes, "בודק את זמני השבת...");
            toolbox.Add(_calendarTools.GetUpcomingHolidaysAsync, "בודק את החגים הקרובים...");

            return toolbox;
        }
    }
}
