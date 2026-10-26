using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IVolunteerService
    {
        Task<List<AvailabilitySlotResponseDto>> GetMyAvailabilityAsync(int userId);
        Task SaveMyAvailabilityAsync(int userId, List<CreateAvailabilitySlotDto> slots);

        Task<List<AdminVolunteerListItemDto>> GetAllForAdminAsync();

        // מחזיר null אם המתנדב לא קיים או שאין למשתמש הרשאה לראות אותו
        Task<VolunteerResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser);

        Task UpdateAsync(int id, UpdateVolunteerDto dto, CurrentUserDto currentUser);
        // תיקון 10: הוספת תפקיד מתנדב לחשבון של המשתמש המחובר (ממתין לאישור מנהל)
        Task<AddRoleResult> AddRoleAsync(int userId, CreateVolunteerDto dto);

        Task<List<AvailabilitySlotResponseDto>> GetAvailabilityAsync(int id, CurrentUserDto currentUser);
        Task SaveAvailabilityAsync(int id, List<CreateAvailabilitySlotDto> slots, CurrentUserDto currentUser);

        // מייל שכבר רשום לא יוצר כלום ולא זורק חריגה - בעל החשבון מקבל מייל (הקונטרולר עונה זהה בשני המקרים)
        Task<RegistrationResult> RegisterWithUserAsync(RegisterVolunteerDto dto);

        // עדכון מיקום חי (GPS) של המתנדב המחובר + שידור למי שעוקב אחרי המסלול
        Task UpdateMyLocationAsync(int userId, double latitude, double longitude);
    }
}
