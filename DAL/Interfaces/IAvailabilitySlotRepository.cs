using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    public interface IAvailabilitySlotRepository : IRepository<AvailabilitySlot>
    {
        // משבצות הזמינות של מתנדב, כולל האזורים שלהן
        Task<List<AvailabilitySlot>> GetByVolunteerIdWithRegionsAsync(int volunteerId);

        // מחליף את כל משבצות הזמינות של המתנדב בשמירה אחת (מחיקה + הוספה יחד)
        Task ReplaceForVolunteerAsync(int volunteerId, IEnumerable<AvailabilitySlot> newSlots);

        // האם יש משבצת זמינות שמשתמשת באזור (כמוצא או כיעד)
        Task<bool> AnyInRegionAsync(int regionId);
    }
}
