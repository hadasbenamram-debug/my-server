using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IDonationRepository : IRepository<Donation>
    {
        // תרומות פתוחות לשיבוץ: Pending, לא פגו, נוצרו היום (לפי היום המקומי), של ספק מאושר
        Task<List<Donation>> GetAvailableDonationsAsync(DateTime nowUtc, DateTime localDayStartUtc);

        Task<List<Donation>> GetAllWithCategoriesAsync();
        Task<Donation?> GetByIdWithCategoriesAsync(int id);

        // התרומה במעקב (Tracked) כולל שורות הקטגוריה - לעדכון
        Task<Donation?> GetForUpdateAsync(int id);

        // תרומה עם המזהה הזה *וגם* של הספק הזה (null = לא קיימת או לא שלו)
        Task<Donation?> GetOwnedForUpdateAsync(int id, int supplierId);

        // התרומה במעקב, עם הערכים העדכניים מהמסד (כולל RowVersion)
        Task<Donation?> GetFreshAsync(int id);

        // תרומות שפג תוקפן או שעבר מועד האיסוף שלהן ועדיין ממתינות / שובצו ולא נאספו
        Task<List<Donation>> GetExpiredOpenAsync(DateTime nowUtc);

        Task<List<Donation>> GetBySupplierIdWithRegionAsync(int supplierId);
        Task<PagedResult<Donation>> GetPageWithSupplierAsync(int skip, int take);

        // נתיבי התמונות ששמורים בתרומות (לניקוי תמונות "יתומות")
        Task<List<string>> GetImageFileNamesAsync();

        // התרומה האחרונה של הספק עם קובץ התמונה הזה (לזיהוי לחיצה כפולה על "פרסם"), כולל קטגוריות, אזור וספק
        Task<Donation?> GetLatestBySupplierAndImageAsync(int supplierId, string image);

        Task<bool> ExistsAsync(int id);
        Task<bool> ExistsForSupplierAsync(int donationId, int supplierId);

        // תיקון 77: האם לספק יש תרומה שעדיין בטיפול (ממתינה, שובצה, נאספה או נמסרה וממתינה לאישור קבלה)
        Task<bool> HasOpenForSupplierAsync(int supplierId);
        Task<bool> AnyInRegionAsync(int regionId);
        Task<int> CountByStatusAsync(DonationStatus status);

        // תיקון 17: סך המשקל (ק"ג) של התרומות שהגיעו לעמותות (Delivered / Completed)
        Task<double> SumDeliveredWeightKgAsync();

        // תיקון 31: כמה תרומות הגיעו לעמותות (Delivered / Completed) - לנתוני "ההשפעה שלנו" בדף הבית
        Task<int> CountDeliveredAsync();

        // תיקון 31: סך המשקל (ק"ג) של התרומות שנמסרו לעמותה מהרגע fromUtc (למשל מתחילת החודש)
        Task<double> SumDeliveredWeightKgSinceAsync(DateTime fromUtc);

        // מחזיר false אם התרומה עודכנה במקביל ע"י בקשה אחרת (RowVersion)
        Task<bool> TryUpdateAsync(Donation donation);

        Task SaveChangesAsync();
    }
}
