using FoodRescue.DTOs.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // הסינון האוטומטי שלפני אישור המנהל (מסמך התיאור: הרשמה -> סינון אוטומטי -> אישור מנהל).
    // התוצאה נשמרת בבקשת האישור (PassedAutomaticValidation + ValidationDetails) כדי שהמנהל יראה אותה.
    public interface IValidationService
    {
        Task<(bool Passed, string Details)> ValidateApplicantAsync(string? idNumber, string? licenseNumber);

        (bool Passed, string Details) ValidateVolunteer(string? idNumber, string? licenseNumber, DateTime? licenseExpiry, DateTime dateOfBirth);
    }

    public class ValidationService : IValidationService
    {
        public Task<(bool Passed, string Details)> ValidateApplicantAsync(string? idNumber, string? licenseNumber)
        {
            var problems = new List<string>();

            // תוקן: בעבר נבדק רק האורך (9 תווים). עכשיו גם ספרת הביקורת.
            if (!IsraeliId.IsValid(idNumber))
                problems.Add("מספר תעודת זהות שגוי (ספרת ביקורת לא תקינה) או חסר.");

            if (!string.IsNullOrWhiteSpace(licenseNumber) && licenseNumber.Trim().Length < 5)
                problems.Add("מספר רישיון נהיגה לא תקין.");

            return Task.FromResult(Result(problems));
        }

        public (bool Passed, string Details) ValidateVolunteer(string? idNumber, string? licenseNumber, DateTime? licenseExpiry, DateTime dateOfBirth)
        {
            var problems = new List<string>();
            var today = DateTime.UtcNow.Date;

            if (!IsraeliId.IsValid(idNumber))
                problems.Add("מספר תעודת זהות שגוי (ספרת ביקורת לא תקינה).");

            if (string.IsNullOrWhiteSpace(licenseNumber) || licenseNumber.Trim().Length < 5)
                problems.Add("מספר רישיון נהיגה חסר או לא תקין.");

            if (licenseExpiry == null)
                problems.Add("לא צוין תוקף רישיון נהיגה.");
            else if (licenseExpiry.Value.Date < today)
                problems.Add("תוקף רישיון הנהיגה פג.");
            else if (licenseExpiry.Value.Date < today.AddDays(30))
                problems.Add("תוקף רישיון הנהיגה יפוג בתוך 30 יום.");

            if (dateOfBirth.Date.AddYears(17) > today)
                problems.Add("גיל המתנדב נמוך מ-17.");

            return Result(problems);
        }

        private static (bool, string) Result(List<string> problems) =>
            problems.Count == 0
                ? (true, "הסינון האוטומטי עבר בהצלחה.")
                : (false, string.Join(" ", problems));
    }
}
