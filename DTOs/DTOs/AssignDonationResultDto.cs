using System.Text.Json.Serialization;

namespace FoodRescue.DTOs
{
    // תוצאת שיבוץ ידני של תרומה (POST api/Matches/assign-donation/{donationId})
    public class AssignDonationResultDto
    {
        public bool Matched { get; set; }

        // מופיעים בתשובה רק כשנוצר שידוך
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MatchId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RouteId { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
