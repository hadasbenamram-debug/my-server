using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface ILoginAttemptRepository : IRepository<LoginAttempt>
    {
        // זמני הניסיונות הכושלים של (מייל, IP) מאז 'since' שאחרי ההתחברות המוצלחת האחרונה מאותו IP
        Task<List<DateTime>> GetRecentFailuresAsync(string email, string? ipAddress, DateTime sinceUtc);

        // זמני הניסיונות הכושלים מ-IP מסוים (לכל המיילים) מאז 'since', מהחדש לישן
        Task<List<DateTime>> GetRecentFailuresByIpAsync(string ipAddress, DateTime sinceUtc, int take);

        Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
    }
}
