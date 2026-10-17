using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IFeedbackRepository : IRepository<Feedback>
    {
        // משובים על שידוכים של המתנדב
        Task<List<Feedback>> GetForVolunteerAsync(int volunteerId);

        // משובים על שידוכים של העמותה או על תרומות של הספק (סינון במסד, לא בזיכרון)
        Task<List<Feedback>> GetForNgoOrSupplierAsync(int ngoId, int supplierId);

        Task<PagedResult<Feedback>> GetPageAsync(int skip, int take);

        Task<bool> ExistsForMatchAsync(int matchId);

        // כל דירוגי המתנדב (Feedback.VolunteerRating) - לחישוב ציון האמינות
        Task<List<int>> GetVolunteerRatingsAsync(int volunteerId);
    }
}
