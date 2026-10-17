using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // פרטי קובץ רישיון לצורך הורדה על ידי מנהל.
    public sealed record LicenseFileInfo(string FullPath, string ContentType, string DownloadName);

    // Service עזר: שמירה וקריאה של קובצי רישיון עסק בתיקייה פרטית (לא תחת wwwroot).
    public interface ILicenseFileService
    {
        // מחזיר את שם הקובץ בלבד (Guid + סיומת), שנשמר במסד.
        // זורק ArgumentException אם הקובץ ריק, גדול מ-5MB, בסיומת לא מותרת או שתוכנו לא תואם לסיומת.
        Task<string> SaveLicenseAsync(IFormFile file, CancellationToken cancellationToken = default);

        // מחזיר null אם הערך במסד לא תקין או שהקובץ לא קיים.
        LicenseFileInfo? GetLicenseFile(string? storedValue);

        // מוחק קובץ רישיון שנשמר (למשל כשההרשמה נכשלה אחרי שמירת הקובץ). ערך לא תקין - מתעלמים.
        void DeleteLicense(string? storedValue);
    }
}
