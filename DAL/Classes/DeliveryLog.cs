using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DAL.Classes
{
    // יומן המשלוח של מסלול: כל עדכון מיקום של המתנדב בזמן הנסיעה, וכל שלב (אישור, יציאה לדרך, איסוף, מסירה...).
    // ממנו העמותה (וגם הספקים והמנהלים) רואים בזמן אמת איפה המתנדב ומה כבר נעשה במסלול.
    public class DeliveryLog
    {
        [Key]
        public int Id { get; set; }

        // הגדרת מזהה מסלול המתנדב כמפתח זר
        public int VolunteerRouteId { get; set; }

        [ForeignKey("VolunteerRouteId")]
        public VolunteerRoute VolunteerRoute { get; set; }

        // סוג הרישום: עדכון מיקום / אירוע במסלול / רישום ידני
        public DeliveryLogType Type { get; set; } = DeliveryLogType.Note;

        // התחנה שהרישום מתייחס אליה (למשל איסוף או מסירה של תרומה מסוימת). null = כל המסלול
        public int? MatchId { get; set; }

        [ForeignKey("MatchId")]
        public Match? Match { get; set; }

        [Required]
        public string StatusDescription { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // מיקום GPS בזמן הרישום (שיתוף מיקום חי) - מאפשר לשחזר את מסלול
        // המתנדב בפועל על גבי Google Maps, לא רק את סטטוס הטקסט.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
