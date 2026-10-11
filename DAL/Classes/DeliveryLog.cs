using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DAL.Classes
{
    public class DeliveryLog
    {
        [Key]
        public int Id { get; set; }

        // הגדרת מזהה מסלול המתנדב כמפתח זר
        public int VolunteerRouteId { get; set; }

        [ForeignKey("VolunteerRouteId")]
        public VolunteerRoute VolunteerRoute { get; set; }

        [Required]
        public string StatusDescription { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // מיקום GPS בזמן הרישום (שיתוף מיקום חי) - מאפשר לשחזר את מסלול
        // המתנדב בפועל על גבי Google Maps, לא רק את סטטוס הטקסט.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}