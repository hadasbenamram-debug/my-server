using System.Linq;

namespace DAL.Classes
{
    public static class DonationExtensions
    {
        // סכום המשקל של כל שורות הקטגוריה בתרומה
        public static double TotalWeightKg(this Donation donation) =>
            donation.DonationFoodCategories?.Sum(dfc => dfc.WeightKg) ?? 0;

        // סכום הכמות של כל שורות הקטגוריה בתרומה
        public static int TotalQuantity(this Donation donation) =>
            donation.DonationFoodCategories?.Sum(dfc => dfc.Quantity) ?? 0;

        // קטגוריה "מייצגת" לצורך אילוץ המגוון בתרמיל - הכבדה ביותר במשקל.
        // (פשטנות מכוונת: תרומה = פריט אחד בתרמיל.)
        public static int PrimaryCategoryId(this Donation donation) =>
            donation.DonationFoodCategories?
                .OrderByDescending(dfc => dfc.WeightKg)
                .Select(dfc => dfc.CategoryId)
                .FirstOrDefault() ?? 0;

        /// <summary>
        /// חדש: מקדם הפגיעות (PerishabilityFactor) הממוצע של התרומה, משוקלל במשקל.
        /// בקוד המקורי השדה FoodCategory.PerishabilityFactor לא הופיע באף נוסחה, למרות שמסמך
        /// התיאור מציין "סוגי המזון" כמרכיב בציון ההתאמה. 1.0 = רגיל, גבוה יותר = מתכלה מהר יותר.
        /// דורש טעינת Category (GetAvailableDonationsAsync כבר עושה Include); אם לא נטענה — 1.0.
        /// </summary>
        public static double AveragePerishability(this Donation donation)
        {
            var rows = donation.DonationFoodCategories;
            if (rows == null || rows.Count == 0) return 1.0;

            double totalWeight = 0, weighted = 0, plainSum = 0;
            foreach (var row in rows)
            {
                double p = row.Category?.PerishabilityFactor ?? 1.0;
                if (p <= 0) p = 1.0;
                double w = row.WeightKg > 0 ? row.WeightKg : 0;
                totalWeight += w;
                weighted += w * p;
                plainSum += p;
            }

            return totalWeight > 0 ? weighted / totalWeight : plainSum / rows.Count;
        }
    }
}
