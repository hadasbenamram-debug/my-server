using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת קטגוריית מזון חדשה ע"י מנהל (ללא מזהה שנקבע אוטומטית בשרת)
    public class CreateFoodCategoryDto
    {
        // תוקן: המסד מקבל עד 50 תווים (FoodCategory.CategoryName) - בעבר 100, ואז 500 במקום 400
        [Required(ErrorMessage = "שם הקטגוריה הוא שדה חובה.")]
        [StringLength(50, ErrorMessage = "שם הקטגוריה יכול להכיל עד 50 תווים.")]
        public string CategoryName { get; set; } = string.Empty;

        [Range(0.1, 10.0, ErrorMessage = "מקדם הריקבון חייב להיות בטווח שבין 0.1 ל-10.0.")]
        public double PerishabilityFactor { get; set; } = 1.0;
    }

    // קטגוריה כפי שהיא חוזרת ללקוח (כולל מזהה - כדי שאפשר יהיה לבחור אותה ברשימה)
    public class FoodCategoryResponseDto
    {
        public int Id { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public double PerishabilityFactor { get; set; }
    }
}
