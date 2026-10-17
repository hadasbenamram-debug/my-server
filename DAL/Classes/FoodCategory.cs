using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public class FoodCategory
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string CategoryName { get; set; }

        // קצב "התכלות" יחסי - משפיע על נוסחת הדחיפות
        public double PerishabilityFactor { get; set; } = 1.0;

        // קשר טבלאות (דרך טבלת הקישור)
        public ICollection<DonationFoodCategory> DonationFoodCategories { get; set; }
    }
}