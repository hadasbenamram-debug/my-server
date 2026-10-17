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
        Task<VolunteerResponseDto> CreateAsync(int userId, CreateVolunteerDto dto);

        Task<List<AvailabilitySlotResponseDto>> GetAvailabilityAsync(int id, CurrentUserDto currentUser);
        Task SaveAvailabilityAsync(int id, List<CreateAvailabilitySlotDto> slots, CurrentUserDto currentUser);

        Task RegisterWithUserAsync(RegisterVolunteerDto dto);

        // עדכון מיקום חי (GPS) של המתנדב המחובר + שידור למי שעוקב אחרי המסלול
        Task UpdateMyLocationAsync(int userId, double latitude, double longitude);
    }
}
