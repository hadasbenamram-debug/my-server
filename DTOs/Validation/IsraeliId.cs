using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace FoodRescue.DTOs.Validation
{
    // בדיקת מספר תעודת זהות ישראלית כולל ספרת ביקורת (ולא רק אורך).
    public static class IsraeliId
    {
        // מחזיר את המספר מנורמל ל-9 ספרות (עם אפסים מובילים), או null אם הוא לא תקין
        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            if (trimmed.Length < 5 || trimmed.Length > 9 || !trimmed.All(char.IsAsciiDigit)) return null;
            return trimmed.PadLeft(9, '0');
        }

        public static bool IsValid(string? value)
        {
            var id = Normalize(value);
            if (id == null || id == "000000000") return false;

            int sum = 0;
            for (int i = 0; i < 9; i++)
            {
                int digit = id[i] - '0';
                int step = digit * ((i % 2) + 1);   // משקלות 1,2,1,2,...
                sum += step > 9 ? step - 9 : step;
            }
            return sum % 10 == 0;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class IsraeliIdAttribute : ValidationAttribute
    {
        public IsraeliIdAttribute() : base("מספר תעודת הזהות אינו תקין (ספרת הביקורת שגויה).") { }

        public override bool IsValid(object? value) =>
            value == null || (value is string s && (string.IsNullOrWhiteSpace(s) || IsraeliId.IsValid(s)));
    }

    // תאריך שחייב להיות היום או בעתיד (למשל תוקף רישיון נהיגה)
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NotInPastAttribute : ValidationAttribute
    {
        public NotInPastAttribute() : base("התאריך כבר עבר.") { }

        public override bool IsValid(object? value) =>
            value is not DateTime d || d.Date >= DateTime.UtcNow.Date;
    }

    // גיל מינימלי לפי תאריך לידה
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MinimumAgeAttribute : ValidationAttribute
    {
        public int Years { get; }

        public MinimumAgeAttribute(int years) : base($"הגיל המינימלי הוא {years}.")
        {
            Years = years;
        }

        public override bool IsValid(object? value)
        {
            if (value is not DateTime birth) return true;
            var today = DateTime.UtcNow.Date;
            if (birth.Date > today) return false;
            return birth.Date.AddYears(Years) <= today;
        }
    }
}
