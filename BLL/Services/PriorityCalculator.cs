using BLL.Services;
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
    ///   חדש: 100 · e^(-דקות עד המועד האחרון / (τ · פגיעות)). ברגע האחרון = 100, ככל שרחוק יותר → 0.
    ///   מזון מתכלה (PerishabilityFactor גבוה) דחוף יותר משאר המזון בכל נקודת זמן, כשנשאר לשניהם אותו זמן.
    ///   המנוע מחשב בזיכרון בכל שימוש ולא תלוי בערך השמור (PriorityScore נשמר רק לתצוגה).
    ///
    ///   תוקן: בעבר τ חולק בפגיעות (τ / פגיעות). פגיעות גבוהה נתנה τ קטן, והעקומה ירדה מהר יותר
    ///   כשמתרחקים מהמועד - כך שבשר עם 120 דקות קיבל 30.1 ושימורים עם 120 דקות קיבלו 71.7, הפוך מהכוונה.
    ///   עכשיו τ מוכפל בפגיעות: בשר 69.1, רגיל 51.3, שימורים 26.4 (τ = 180, 120 דקות לפני המועד).
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
            double tau = timeConstantMinutes * perishability;   // תוקן: היה חילוק - מזון מתכלה קיבל דחיפות נמוכה יותר
            double score = MaxScore * Math.Exp(-minutesLeft / tau);

            return Math.Clamp(score, 0.0, MaxScore);
        }
    }
}
