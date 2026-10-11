using System.Threading.Tasks;

namespace BLL.Services
{
    public interface IValidationService
    {
        Task<(bool Passed, string Details)> ValidateApplicantAsync(string idNumber, string licenseNumber);
    }

    public class ValidationService : IValidationService
    {
        public async Task<(bool Passed, string Details)> ValidateApplicantAsync(string idNumber, string licenseNumber)
        {
            // בדיקת תקינות בסיסית של מספר תעודת זהות בשרת
            if (string.IsNullOrWhiteSpace(idNumber) || idNumber.Length != 9)
            {
                return await Task.FromResult((false, "מספר תעודת זהות שגוי או חסר."));
            }

            // בדיקת רישיון נהיגה במידת הצורך
            if (!string.IsNullOrWhiteSpace(licenseNumber) && licenseNumber.Length < 5)
            {
                return await Task.FromResult((false, "מספר רישיון נהיגה לא תקין."));
            }

            return await Task.FromResult((true, "האימות האוטומטי עבר בהצלחה בשרת."));
        }
    }
}