using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ISettingsService
    {
        // הערכים בפועל (ברירת מחדל + מה שהמנהל שמר)
        SystemSettingsDto GetCurrent();

        // שומר במסד ומעדכן מיד את מנוע השידוכים
        Task<SystemSettingsDto> UpdateAsync(SystemSettingsDto changes, int? updatedByUserId);
    }
}
