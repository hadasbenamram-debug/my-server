using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace BLL.Agent
{
    // הכלים שהמודל מקבל בשאלה הנוכחית, ולכל כלי - ההודעה שהמשתמש רואה בזמן שהוא רץ ("בודק את התרומות שלך...").
    // AIFunctionFactory.Create קורא את שם המתודה, את ה-[Description] ואת הפרמטרים, ובונה מהם את התיאור שנשלח למודל.
    public sealed class AgentToolbox
    {
        private readonly Dictionary<string, string> _statusByName = new(StringComparer.Ordinal);

        public List<AITool> Tools { get; } = new();

        // שמות הכלים כפי שהמודל רואה אותם (Async מוסר מהשם: GetMyDonationsAsync -> GetMyDonations)
        public IEnumerable<string> Names => _statusByName.Keys;

        public void Add(Delegate method, string statusText)
        {
            var function = AIFunctionFactory.Create(method);
            Tools.Add(function);
            _statusByName[function.Name] = statusText;
        }

        public string StatusFor(string toolName) =>
            _statusByName.TryGetValue(toolName, out var status) ? status : "בודק במערכת...";
    }
}
