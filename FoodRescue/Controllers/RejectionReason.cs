using System.Text.Json;

namespace FoodRescue.Controllers
{
    // סיבת דחייה: הלקוח הקיים שולח מחרוזת JSON ("סיבה"), ואפשר גם אובייקט { "reason": "..." }.
    public static class RejectionReason
    {
        public static string? Read(JsonElement body) => body.ValueKind switch
        {
            JsonValueKind.String => body.GetString(),
            JsonValueKind.Object when body.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String => r.GetString(),
            JsonValueKind.Object when body.TryGetProperty("Reason", out var r) && r.ValueKind == JsonValueKind.String => r.GetString(),
            _ => null
        };
    }
}
