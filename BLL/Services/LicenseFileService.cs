using BLL.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Services
{
    // Service עזר: שמירה וקריאה של קובצי רישיון עסק.
    // הקבצים נשמרים בתיקייה פרטית מחוץ ל-wwwroot (מוגדרת ב-appsettings.json תחת LicensesFolder),
    // ולכן UseStaticFiles לא חושף אותם. הגישה אליהם רק דרך endpoint של מנהל.
    public sealed class LicenseFileService : ILicenseFileService
    {
        private const long MaxFileSize = 5 * 1024 * 1024;

        private static readonly Dictionary<string, string> ContentTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = "application/pdf",
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png"
            };

        private readonly string _licensesFolder;
        private readonly ILogger<LicenseFileService> _logger;

        public LicenseFileService(string licensesFolder, ILogger<LicenseFileService> logger)
        {
            if (string.IsNullOrWhiteSpace(licensesFolder))
                throw new ArgumentException("Licenses folder must be configured.", nameof(licensesFolder));

            _licensesFolder = Path.GetFullPath(licensesFolder);
            _logger = logger;

            Directory.CreateDirectory(_licensesFolder);
        }

        public async Task<string> SaveLicenseAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length <= 0)
                throw new ArgumentException("חובה לצרף קובץ רישיון עסק.");

            if (file.Length > MaxFileSize)
                throw new ArgumentException("גודל קובץ הרישיון המרבי הוא 5MB.");

            string extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();

            if (!ContentTypes.ContainsKey(extension))
                throw new ArgumentException("סיומת קובץ לא חוקית. יש להעלות קובץ PDF או תמונה בלבד.");

            await using var input = file.OpenReadStream();

            // בדיקת תוכן: הסיומת לבדה לא מספיקה, בודקים את הבתים הראשונים של הקובץ.
            if (!await HasValidSignatureAsync(input, extension, cancellationToken))
                throw new ArgumentException("תוכן הקובץ אינו תואם לסוג הקובץ.");

            // שם הקובץ נוצר בשרת. השם שהמשתמש נתן לא נשמר.
            string fileName = $"{Guid.NewGuid():N}{extension}";
            string fullPath = Path.Combine(_licensesFolder, fileName);

            try
            {
                input.Position = 0;
                await using var output = new FileStream(
                    fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await input.CopyToAsync(output, 81920, cancellationToken);
            }
            catch
            {
                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(cleanupException, "Failed to clean up an incomplete license upload.");
                }

                throw;
            }

            // במסד נשמר רק שם הקובץ - בלי נתיב ובלי כתובת.
            return fileName;
        }

        public LicenseFileInfo? GetLicenseFile(string? storedValue)
        {
            if (string.IsNullOrWhiteSpace(storedValue))
                return null;

            // תמיכה גם בערכים ישנים שנשמרו כ-"/Files/licenses/<name>".
            string fileName = Path.GetFileName(storedValue.Replace('\\', '/'));
            string extension = Path.GetExtension(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);

            // רק שמות שהשרת עצמו יצר (Guid + סיומת מותרת). כל ערך אחר נדחה - הגנה מפני path traversal.
            if (!ContentTypes.TryGetValue(extension, out var contentType) || !Guid.TryParse(stem, out _))
                return null;

            string fullPath = Path.Combine(_licensesFolder, fileName);
            if (!File.Exists(fullPath))
                return null;

            return new LicenseFileInfo(fullPath, contentType, fileName);
        }

        public void DeleteLicense(string? storedValue)
        {
            var file = GetLicenseFile(storedValue);
            if (file == null)
                return;

            try
            {
                File.Delete(file.FullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete license file {File}.", file.DownloadName);
            }
        }

        private static async Task<bool> HasValidSignatureAsync(Stream input, string extension, CancellationToken ct)
        {
            var header = new byte[8];
            int read = await input.ReadAsync(header.AsMemory(0, header.Length), ct);

            return extension switch
            {
                ".pdf" => read >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46, // %PDF
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
                _ => false
            };
        }
    }
}
