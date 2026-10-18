using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public sealed record ImageAnalysisResult(
        string? AiParsedData,
        string? Message);

    public interface IImageAnalysisService
    {
        // categories: הקטגוריות כפי שהן במסד. נשלחות ל-AI כדי שיחזיר רק מזהים שקיימים בפועל.
        Task<ImageAnalysisResult> AnalyzeAsync(
            IFormFile image,
            IReadOnlyList<CategoryOptionDto> categories,
            CancellationToken cancellationToken = default);
    }
}
