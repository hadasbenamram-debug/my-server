using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // Service עזר: המקום היחיד במערכת שבודק, שומר, מוחק ומונה קבצי תמונה.
    // שאר המחלקות רק קוראות לו. הוא מחזיר ומקבל שמות קבצים בלבד (בלי נתיב).
    public interface IImageService
    {
        // בודק את הקובץ, שומר אותו בשם חדש וייחודי (Guid) ומחזיר רק את שם הקובץ
        Task<string> Save(
            IFormFile image,
            CancellationToken cancellationToken = default);

        Task Delete(string? fileName);

        // שמות התמונות בתיקייה שנוצרו לפני הזמן שצוין (לניקוי תמונות שלא שויכו לאף תרומה)
        IReadOnlyList<string> ListFileNames(DateTime createdBeforeUtc);
    }
}
