using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ISupportService
    {
        // שולח את הפנייה לתיבת התמיכה, ואישור קבלה לפונה
        Task SendContactMessageAsync(ContactMessageDto dto, int? userId);
    }
}
