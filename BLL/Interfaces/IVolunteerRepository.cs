using DAL.Classes;

namespace BLL.Interfaces
{
    public interface IVolunteerRepository : IRepository<Volunteer>
    {
        Task<IEnumerable<Volunteer>> GetAvailableVolunteersAsync();
        Task<List<Volunteer>> GetAllActiveAsync();
        Task<Volunteer> GetByUserIdAsync(int userId);
    }
}