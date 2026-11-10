using DAL.Classes;
using FoodRescue.DTOs.Validation;
using System.Linq;

namespace BLL.Services
{
    /// <summary>
    /// תיקון 32: תעודת הזהות שכבר שמורה בחשבון - כדי שמי שמוסיף לעצמו תפקיד (למשל ספק שרוצה גם להתנדב)
    /// לא יצטרך להזין אותה שוב.
    ///
    /// מאיפה היא נלקחת:
    ///   * פרופיל מתנדב - IdNumber (נבדק בהרשמה, כולל ספרת ביקורת).
    ///   * פרופיל ספק - מספר העוסק (TaxId), רק כשהוא מספר זהות של אדם: עוסק מורשה / עוסק פטור רשומים במע"מ
    ///     לפי מספר תעודת הזהות שלהם. מספרי תאגידים (חברה 51, חברה ציבורית 52, שותפות 55, עמותה / חל"צ 58 וכו')
    ///     מתחילים ב-5 - הם לא מספר זהות של אדם, ולכן לא נלקחים (ואז מבקשים להזין ת"ז).
    ///
    /// המספר המלא לא יוצא מהשרת: הלקוח מקבל רק גרסה מוסתרת (Mask), והשרת ממלא את המספר בעצמו.
    /// </summary>
    public static class AccountIdNumber
    {
        // ת"ז שמורה בחשבון (9 ספרות), או null אם אין
        public static string? Find(Volunteer? volunteer, Supplier? supplier)
        {
            if (volunteer != null && IsraeliId.IsValid(volunteer.IdNumber))
                return IsraeliId.Normalize(volunteer.IdNumber);

            return supplier != null && IsPersonalId(supplier.TaxId)
                ? IsraeliId.Normalize(DigitsOnly(supplier.TaxId))
                : null;
        }

        // האם מספר העוסק הוא מספר זהות של אדם (ולא מספר של תאגיד)
        public static bool IsPersonalId(string? number)
        {
            var normalized = IsraeliId.Normalize(DigitsOnly(number));
            return normalized != null && IsraeliId.IsValid(normalized) && normalized[0] != '5';
        }

        // "•••••6789" - מספיק כדי לזהות את המספר במסך, בלי לחשוף אותו
        public static string Mask(string idNumber) =>
            idNumber.Length <= 4
                ? new string('•', idNumber.Length)
                : new string('•', idNumber.Length - 4) + idNumber[^4..];

        // מספר עוסק נכתב לפעמים עם מקפים או רווחים (51-123456-7)
        private static string? DigitsOnly(string? value) =>
            value == null ? null : new string(value.Where(c => c != '-' && c != ' ').ToArray());
    }
}
