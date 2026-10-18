using BLL.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Services
{
    public sealed class ImageService : IImageService
    {
        private const long MaxFileSize = 5 * 1024 * 1024;
        private const int MaxDimension = 8000;

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

        private readonly string _imagesFolder;
        private readonly ILogger<ImageService> _logger;

        public ImageService(
            string imagesFolder,
            ILogger<ImageService> logger)
        {
            if (string.IsNullOrWhiteSpace(imagesFolder))
                throw new ArgumentException(
                    "Images folder must be configured.",
                    nameof(imagesFolder));

            _imagesFolder = Path.GetFullPath(imagesFolder);
            _logger = logger;

            Directory.CreateDirectory(_imagesFolder);
        }

        public async Task<string> Save(
            IFormFile image,
            CancellationToken cancellationToken = default)
        {
            if (image == null || image.Length <= 0)
                throw new ArgumentException("קובץ התמונה ריק.");

            if (image.Length > MaxFileSize)
                throw new ArgumentException(
                    "גודל התמונה המרבי הוא 5MB.");

            string extension =
                Path.GetExtension(Path.GetFileName(image.FileName));

            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException(
                    "סיומת התמונה אינה נתמכת.");

            await using var input = image.OpenReadStream();

            var info = await Image.IdentifyAsync(input, cancellationToken);

            if (info == null)
                throw new ArgumentException("הקובץ שהועלה אינו תמונה תקינה.");

            if (info.Width > MaxDimension || info.Height > MaxDimension)
                throw new ArgumentException("מימדי התמונה גדולים מדי.");

            input.Position = 0;
            var format = await Image.DetectFormatAsync(input, cancellationToken);
            input.Position = 0;

            var allowedFormats = new[] { "JPEG", "PNG", "WEBP" };

            if (!allowedFormats.Contains(format.Name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("פורמט התמונה אינו נתמך.");

            cancellationToken.ThrowIfCancellationRequested();

            // שם הקובץ נוצר בשרת ולא נלקח מהלקוח.
            string fileName =
                $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

            string fullPath = GetSafePath(fileName);

            try
            {
                await using var output = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous);

                input.Position = 0;

                await input.CopyToAsync(
                    output,
                    81920,
                    cancellationToken);

                await output.FlushAsync(cancellationToken);

                return fileName;
            }
            catch
            {
                // מנקים קובץ חלקי במקרה של כישלון בשמירה.
                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "Failed to clean up an incomplete image upload.");
                }

                throw;
            }
        }

        public Task Delete(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Task.CompletedTask;

            try
            {
                string fullPath = GetSafePath(fileName);

                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch (ArgumentException)
            {
                _logger.LogWarning(
                    "Rejected deletion request for an invalid image filename.");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete an image file.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Insufficient permissions to delete an image file.");
            }

            return Task.CompletedTask;
        }

        private string GetSafePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(
                    fileName,
                    Path.GetFileName(fileName),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Invalid image filename.",
                    nameof(fileName));
            }

            string extension = Path.GetExtension(fileName);

            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException(
                    "Invalid image extension.",
                    nameof(fileName));

            string stem = Path.GetFileNameWithoutExtension(fileName);

            if (stem.Length != 32 ||
                !Guid.TryParseExact(stem, "N", out _))
            {
                throw new ArgumentException(
                    "Invalid image filename format.",
                    nameof(fileName));
            }

            return Path.Combine(_imagesFolder, fileName);
        }
    }
}