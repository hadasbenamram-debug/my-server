using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    // תוקן: הוסר Id עצמאי - היה עמודה "מתה" בפועל (לא Identity, לא PK, לא Unique)
    // כי ה-PK האמיתי כבר מוגדר כזוג מורכב (DonationId, CategoryId) ב-OnModelCreating.
    public class DonationFoodCategory
    {
        [Required]
        public int DonationId { get; set; }
        public Donation Donation { get; set; }

        [Required]
        public int CategoryId { get; set; }
        public FoodCategory Category { get; set; }

        [Required]
        public int Quantity { get; set; }

        [Required]
        public double WeightKg { get; set; }
    }
}