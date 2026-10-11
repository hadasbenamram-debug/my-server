using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירה/הוספה של קטגוריה לתרומה (ללא DonationId שנקבע בשרת)
    public class CreateDonationFoodCategoryDto
    {
        [Required(ErrorMessage = "יש לציין מזהה קטגוריה.")]
        public int CategoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "הכמות חייבת להיות לפחות 1.")]
        public int Quantity { get; set; }

        [Range(0.01, 10000, ErrorMessage = "המשקל חייב להיות גדול מאפס.")]
        public double WeightKg { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class DonationFoodCategoryResponseDto
    {
        public int DonationId { get; set; }
        public int CategoryId { get; set; }
        public FoodCategoryResponseDto Category { get; set; }
        public int Quantity { get; set; }
        public double WeightKg { get; set; }
    }
}