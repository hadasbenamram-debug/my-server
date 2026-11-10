using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    // תיקון 31: נתוני "ההשפעה שלנו" בדף הבית - אמיתיים, מהמסד
    public interface IPublicStatsService
    {
        Task<PublicStatsDto> GetAsync();
    }
}
