using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ISystemSettingRepository
    {
        Task<List<SystemSetting>> GetAllAsync();
        Task UpsertManyAsync(IReadOnlyDictionary<string, string> values, int? updatedByUserId);
    }
}
