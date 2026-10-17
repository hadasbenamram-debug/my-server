namespace FoodRescue.DTOs
{
    // קטגוריה כפי שנשלחת לשירות ניתוח התמונה (מזהה + שם)
    public class CategoryOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // נקודה על המפה (GET api/map/all-entities)
    public class MapEntityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
