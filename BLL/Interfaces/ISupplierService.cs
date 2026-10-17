using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ISupplierService
    {
        Task<IEnumerable<SupplierResponseDto>> GetAllAsync();
        Task<List<AdminSupplierListItemDto>> GetAdminListAsync();

        // מחזיר null אם הספק לא קיים או שאין למשתמש הרשאה לראות אותו (לפי מזהה ספק בלבד)
        Task<SupplierResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser);

        // מחזיר null אם למשתמש המחובר אין פרופיל ספק
        Task<SupplierResponseDto?> GetCurrentAsync(int userId);

        Task<SupplierResponseDto> CreateAsync(int userId, CreateSupplierDto dto);
        Task<SupplierResponseDto> RegisterWithUserAsync(RegisterSupplierDto dto);

        // קובץ הרישיון של ספק (למנהל בלבד). מחזיר null אם הספק או הקובץ לא נמצאו.
        Task<LicenseFileInfo?> GetLicenseFileAsync(int supplierId);
    }
}
