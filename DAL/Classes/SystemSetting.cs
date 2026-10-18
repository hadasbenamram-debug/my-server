using System;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    // הגדרת מערכת שהמנהל שינה (api/settings). דורסת את ערך ברירת המחדל מ-appsettings
    // (MatchingPolicy) ונקראת ע"י מנוע השידוכים בכל ריצה.
    public class SystemSetting
    {
        [Key, MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required, MaxLength(400)]
        public string Value { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int? UpdatedByUserId { get; set; }
    }
}
