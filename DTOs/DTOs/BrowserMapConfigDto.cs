namespace FoodRescue.DTOs
{
    // הגדרות המפה האינטראקטיבית בדפדפן (GET api/map/browser-config) - רק למשתמש מחובר.
    // המפתח כאן הוא מפתח הדפדפן (Maps JavaScript API בלבד, מוגבל לכתובות האתר) - לא המפתח של השרת.
    public class BrowserMapConfigDto
    {
        // false = אין מפתח דפדפן מוגדר בשרת (או שהוגדר בטעות המפתח של השרת). הלקוח מציג אז את תמונת המפה מהשרת.
        public bool Enabled { get; set; }

        public string? ApiKey { get; set; }

        // Map ID מ-Google Cloud (נדרש לסיכות המתקדמות). לא סודי. ברירת מחדל: DEMO_MAP_ID לפיתוח.
        public string MapId { get; set; } = "DEMO_MAP_ID";

        public string Language { get; set; } = "he";
        public string Region { get; set; } = "IL";
    }
}
