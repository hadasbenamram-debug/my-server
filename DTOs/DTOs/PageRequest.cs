using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // עימוד לרשימות ארוכות: ?page=1&pageSize=50. סך כל הרשומות חוזר בכותרת X-Total-Count.
    public class PageRequest
    {
        public const int DefaultPageSize = 100;
        public const int MaxPageSize = 500;

        [Range(1, int.MaxValue, ErrorMessage = "מספר העמוד חייב להיות 1 ומעלה.")]
        public int Page { get; set; } = 1;

        [Range(1, MaxPageSize, ErrorMessage = "גודל העמוד חייב להיות בין 1 ל-500.")]
        public int PageSize { get; set; } = DefaultPageSize;

        public int Skip => (Page - 1) * PageSize;
    }

    public class PagedResponse<T>
    {
        public System.Collections.Generic.List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
