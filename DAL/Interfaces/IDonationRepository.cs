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

        Task<bool> ExistsAsync(int id);
        Task<bool> ExistsForSupplierAsync(int donationId, int supplierId);
        Task<bool> AnyInRegionAsync(int regionId);
        Task<int> CountByStatusAsync(DonationStatus status);

        // מחזיר false אם התרומה עודכנה במקביל ע"י בקשה אחרת (RowVersion)
        Task<bool> TryUpdateAsync(Donation donation);

        Task SaveChangesAsync();
    }
}
