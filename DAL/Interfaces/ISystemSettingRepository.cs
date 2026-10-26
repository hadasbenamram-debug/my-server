using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ISystemSettingRepository
    {
        Task<List<SystemSetting>> GetAllAsync();
        // nowUtc: זמן העדכון נקבע ב-Service (חישוב תאריך הוא לוגיקה עסקית), ה-Repository רק שומר
        Task UpsertManyAsync(IReadOnlyDictionary<string, string> values, int? updatedByUserId, DateTime nowUtc);
    }
}
