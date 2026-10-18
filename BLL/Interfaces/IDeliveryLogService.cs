using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IDeliveryLogService
    {
        // מנהל רואה הכל (בעמודים), מתנדב רואה רק לוגים של המסלולים שלו
        Task<List<DeliveryLogResponseDto>> GetLogsAsync(CurrentUserDto currentUser, PageRequest page);

        // יצירת לוג + עדכון המיקום החי של המתנדב (ושידור בזמן אמת)
        Task<DeliveryLogResponseDto> CreateAsync(CreateDeliveryLogDto dto, CurrentUserDto currentUser);
    }
}
