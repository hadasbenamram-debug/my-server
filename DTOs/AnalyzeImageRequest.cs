using Microsoft.AspNetCore.Http;

namespace FoodRescue.DTOs
{
    public class AnalyzeImageRequest
    {
        public IFormFile Image { get; set; }
    }
}