using DAL.Classes;
using System;

namespace BLL.Algorithms
{
    /// <summary>
    /// קובץ חדש. חישוב דחיפות (0-100) כפונקציה טהורה של "כמה זמן נשאר עד המועד האחרון לאיסוף".
    ///
    /// למה לא ממשיכים עם הנוסחה הישנה (PriorityDecayScheduler):
    ///   ישן: ((total - remaining) / total)^2 * 100, כאשר total = ExpirationTime - CreatedAt.
    ///   הבעיה: הדחיפות יחסית לאורך חלון התרומה ולא לזמן שנשאר בפועל. תרומה שנוצרה לפני שעה עם 30 דקות
    ///   עד התפוגה קיבלה ציון נמוך בהרבה מתרומה שנוצרה לפני 12 שעות עם אותן 30 דקות. בנוסף, הציון נשמר
    ///   ב-DB כל 2 דקות על ישות עם RowVersion — מה שיצר התנגשויות מקביליות מול מנוע השידוכים.
    ///
    ///   חדש: 100 · e^(-דקות עד המועד האחרון / (τ / פגיעות)). ברגע האחרון = 100, ככל שרחוק יותר → 0.
    ///   מזון מתכלה (PerishabilityFactor גבוה) "מזדקן" מהר יותר. מחושב בזיכרון בכל שימוש – אין כתיבה ל-DB.
    /// </summary>
    public static class PriorityCalculator
    {
        public const double MaxScore = 100.0;

        public static double Calculate(Donation donation, DateTime asOfUtc, double timeConstantMinutes = 180.0)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));
            if (timeConstantMinutes <= 0) timeConstantMinutes = 180.0;

            double minutesLeft = (NgoAvailabilityHelper.GetPickupDateTime(donation) - asOfUtc).TotalMinutes;
            if (minutesLeft <= 0) return MaxScore;

            double perishability = Math.Max(0.1, donation.AveragePerishability());
            double tau = timeConstantMinutes / perishability;
            double score = MaxScore * Math.Exp(-minutesLeft / tau);

            return Math.Clamp(score, 0.0, MaxScore);
        }
    }
}
