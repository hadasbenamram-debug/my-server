using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Services
{
    public sealed class GeminiImageAnalysisService : IImageAnalysisService
    {
        private const string DefaultModel = "gemini-2.5-flash";
        private const int MaxRetries = 3;
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(45);

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiImageAnalysisService> _logger;

        public GeminiImageAnalysisService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiImageAnalysisService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ImageAnalysisResult> AnalyzeAsync(
            IFormFile image,
            IReadOnlyList<CategoryOptionDto> categories,
            CancellationToken cancellationToken = default)
        {
            var apiKey = _configuration["Gemini:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ImageAnalysisResult(
                    null,
                    "שירות ניתוח התמונה אינו מוגדר. נא למלא ידנית.");
            }

            // בלי קטגוריות במסד אין לאיזה מזהה לשייך את המוצרים - אין טעם לפנות ל-AI.
            if (categories == null || categories.Count == 0)
            {
                return new ImageAnalysisResult(
                    null,
                    "לא הוגדרו קטגוריות מזון במערכת. נא לפנות למנהל.");
            }

            string base64Image;

            try
            {
                base64Image = await PrepareImageAsync(image, cancellationToken);
            }
            catch (Exception ex) when (
                ex is ImageFormatException ||
                ex is IOException ||
                ex is ArgumentException)
            {
                return new ImageAnalysisResult(
                    null,
                    "לא ניתן לעבד את התמונה שהועלתה.");
            }

            var model = _configuration["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model))
                model = DefaultModel;

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = BuildPrompt(categories) },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = "image/jpeg",
                                    data = base64Image
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var jsonBody = JsonSerializer.Serialize(body);
            var delayMs = 1000;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                bool isLastAttempt = attempt == MaxRetries;

                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"v1beta/models/{model}:generateContent");

                    // המפתח עובר ב-header ולא ב-URL, כדי שלא יופיע בלוגים.
                    request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey?.Trim());
                    request.Content = new StringContent(
                        jsonBody,
                        Encoding.UTF8,
                        "application/json");

                    using var cts =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);

                    cts.CancelAfter(AttemptTimeout);

                    using var response =
                        await _httpClient.SendAsync(request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseString =
                            await response.Content.ReadAsStringAsync(
                                cancellationToken);

                        return ParseResponse(responseString);
                    }

                    // רושמים קוד סטטוס בלבד, בלי גוף התשובה ובלי כתובת.
                    _logger.LogWarning(
                        "Gemini API returned status {StatusCode} (attempt {Attempt}).",
                        (int)response.StatusCode,
                        attempt);

                    if (!IsTransient(response.StatusCode))
                    {
                        return new ImageAnalysisResult(
                            null,
                            "שירות ניתוח התמונות לא קיבל את הבקשה. נא למלא ידנית.");
                    }
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "Gemini API request timed out (attempt {Attempt}).",
                        attempt);

                    if (isLastAttempt)
                    {
                        return new ImageAnalysisResult(
                            null,
                            "חריגת זמן בעיבוד התמונה מול שירות ה-AI. נא למלא ידנית.");
                    }
                }
                catch (HttpRequestException)
                {
                    _logger.LogWarning(
                        "Gemini API request failed (attempt {Attempt}).",
                        attempt);

                    if (isLastAttempt)
                    {
                        return new ImageAnalysisResult(
                            null,
                            "אין תקשורת עם שירות ה-AI. נא למלא ידנית.");
                    }
                }

                if (!isLastAttempt)
                {
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                }
            }

            return new ImageAnalysisResult(
                null,
                "שגיאה בשירות ניתוח התמונות. נא למלא ידנית.");
        }

        private static bool IsTransient(HttpStatusCode status) =>
            (int)status >= 500 ||
            status == HttpStatusCode.RequestTimeout ||
            (int)status == 429;

        private static async Task<string> PrepareImageAsync(
            IFormFile image,
            CancellationToken cancellationToken)
        {
            await using var input = image.OpenReadStream();

            using var loaded = await Image.LoadAsync(input, cancellationToken);

            loaded.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(1024, 1024),
                Mode = ResizeMode.Max
            }));

            await using var output = new MemoryStream();

            await loaded.SaveAsJpegAsync(
                output,
                new JpegEncoder { Quality = 75 },
                cancellationToken);

            return Convert.ToBase64String(output.ToArray());
        }

        private ImageAnalysisResult ParseResponse(string responseString)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseString);

                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                text = StripCodeFence(text);

                // מוודאים שהתשובה היא JSON תקין לפני שמחזירים אותה לקליינט
                if (string.IsNullOrWhiteSpace(text))
                    throw new JsonException("Empty response text.");

                using (JsonDocument.Parse(text)) { }

                return new ImageAnalysisResult(text, null);
            }
            catch (Exception ex) when (
                ex is JsonException ||
                ex is KeyNotFoundException ||
                ex is InvalidOperationException ||
                ex is IndexOutOfRangeException)
            {
                _logger.LogWarning("Could not parse the Gemini API response.");

                return new ImageAnalysisResult(
                    null,
                    "לא ניתן לפענח את תשובת שירות ניתוח התמונות. נא למלא ידנית.");
            }
        }

        // מסיר גדר markdown (```json ... ```) אם Gemini הוסיף אותה, בלי לגעת בתוכן עצמו.
        // (בעבר היה כאן Replace("", "") - שזורק ArgumentException על כל תשובה תקינה.)
        private static string? StripCodeFence(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();

            if (!text.StartsWith("```", StringComparison.Ordinal))
                return text;

            int firstNewline = text.IndexOf('\n');
            int closingFence = text.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewline < 0 || closingFence <= firstNewline)
                return text.Trim('`').Trim();

            return text.Substring(firstNewline + 1, closingFence - firstNewline - 1).Trim();
        }

        // רשימת הקטגוריות נבנית מהמסד בכל בקשה, כך שהמזהים תמיד תואמים לטבלה.
        private static string BuildPrompt(IReadOnlyList<CategoryOptionDto> categories)
        {
            var categoryLines = string.Join(
                "\n",
                categories.Select(c => $"{c.Id} = {c.Name}"));

            return PromptBeforeCategories + categoryLines + PromptAfterCategories;
        }

        private const string PromptBeforeCategories = @"You are a precise and highly reliable AI vision assistant designed to support a food rescue and donation management system. Your primary responsibility is to analyze images of food donations provided by users and extract structured data for distinct food categories visible in the image to pre-fill donation forms efficiently.

Food Validation Check:
First, inspect the image carefully. If the image does not contain food items (e.g., it's a picture of jewelry, a person, a car, furniture, a document, or any non-food object), you must output EXACTLY this JSON array with an error message:
[ { ""error"": ""נא להעלות תמונה של אוכל לתרומה."" } ]

Strict Database Compliance:
If the image does contain food, you must strictly adhere to the established database schema and predefined categories. When identifying category IDs, you are limited exclusively to the following official database mappings:
";

        private const string PromptAfterCategories = @"
If you encounter any ambiguity, select the closest matching ID from this list only, and never invent or output any unlisted category IDs.

Data Extraction & Aggregation Rules:
1. Identify all distinct food categories present in the image.
2. Each category ID must appear at most once. If multiple items belong to the same category, combine their quantities and total weights into a single object for that category ID.
3. Only include a category if you can actually see food of that category in the image.
4. For each category object, provide:
   - ""categoryId"": integer from the list above.
   - ""quantity"": positive integer representing the total combined number of items, containers or trays for this category.
   - ""weightKg"": positive decimal estimation of the total combined weight in kilograms for this category.
   - ""expirationTime"": ISO 8601 date string ONLY IF fully visible and clear on packaging. If not clear or missing, output ""UNKNOWN"".

Output Format:
Return ONLY a valid JSON array containing these aggregated category objects (or the error object if not food). Do not include markdown or text outside the JSON array.";
    }
}
