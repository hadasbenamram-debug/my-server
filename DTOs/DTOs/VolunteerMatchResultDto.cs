using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO תוצאה להצגה בשרת עבור אלגוריתם ההתאמה (Knapsack / Matching)
    public class VolunteerMatchResultResponseDto
    {
        [Required(ErrorMessage = "יש לציין מזהה מתנדב.")]
        public int VolunteerId { get; set; }

        [StringLength(100, ErrorMessage = "שם המתנדב יכול להכיל עד 100 תווים.")]
        public string VolunteerName { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "ציון ההתאמה חייב להיות חיובי.")]
        public double MatchScore { get; set; }

        [Range(0, 10000, ErrorMessage = "המרחק בקילומטרים חייב להיות מספר חיובי.")]
        public double DistanceKm { get; set; } // מרחק בקילומטרים ממיקום התרומה

        public bool IsSelected { get; set; }  // האם נבחר סופית על ידי אלגוריתם ה-Knapsack
    }
}