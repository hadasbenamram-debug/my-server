using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת קטגוריית מזון חדשה ע"י מנהל (ללא מזהה שנקבע אוטומטית בשרת)
    public class CreateFoodCategoryDto
    {
        [Required(ErrorMessage = "שם הקטגוריה הוא שדה חובה.")]
        [StringLength(100, ErrorMessage = "שם הקטגוריה יכול להכיל עד 100 תווים.")]
        public string CategoryName { get; set; }

        [Range(0.1, 10.0, ErrorMessage = "מקדם הריקבון חייב להיות בטווח שבין 0.1 ל-10.0.")]
        public double PerishabilityFactor { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class FoodCategoryResponseDto
    {
        public int Id { get; set; }
        public string CategoryName { get; set; }
        public double PerishabilityFactor { get; set; }
    }
}