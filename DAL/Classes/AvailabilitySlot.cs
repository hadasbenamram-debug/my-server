using DAL.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace DAL
{
    public class AvailabilitySlot
    {
        [Key]
        public int Id { get; set; }

        public int VolunteerId { get; set; }
        public Volunteer Volunteer { get; set; }

        // אזור המוצא/היחיד. שם השדה נשאר RegionId (ולא הוסב ל-FromRegionId) כדי לא
        // לשבור כל קוד/query קיים שמניח את השם הזה עבור זמינות רגילה.
        [Required] public int RegionId { get; set; }
        [ForeignKey("RegionId")] public Region Region { get; set; }

        // נוסף: null = זמינות רגילה באזור יחיד. מולא = הצהרת הובלה בין-אזורית מ-RegionId
        // ל-ToRegionId. חייב להיות שונה מ-RegionId כשמולא (נאכף ב-BLL, לא ב-DB - ראו הערה
        // "לא באוכפים ב-DB" בהמשך המסמך שלנו).
        public int? ToRegionId { get; set; }
        [ForeignKey("ToRegionId")] public Region ToRegion { get; set; }

        [Required] public DayOfWeek Day { get; set; }
        [Required] public TimeSpan StartTime { get; set; }
        [Required] public TimeSpan EndTime { get; set; }
    }
}