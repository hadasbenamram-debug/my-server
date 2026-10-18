using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    public class CreateSupplierDto
    {
        // תוקן: המסד מקבל עד 100 תווים (Supplier.BusinessName) - בעבר 150
        [Required(ErrorMessage = "שם העסק הוא שדה חובה.")]
        [StringLength(100, ErrorMessage = "שם העסק יכול להכיל עד 100 תווים.")]
        public string BusinessName { get; set; } = string.Empty;

        [Required(ErrorMessage = "כתובת העסק היא שדה חובה.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        // תוקן: המסד מקבל עד 20 תווים (Supplier.TaxId) - בעבר 50
        [Required(ErrorMessage = "יש להזין ח.פ / מספר עוסק.")]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "ח.פ / מספר עוסק יכול להכיל 5-20 תווים.")]
        public string TaxId { get; set; } = string.Empty;

        [Required(ErrorMessage = "קובץ רישיון עסק הוא שדה חובה.")]
        public IFormFile LicenseFile { get; set; } = null!;
    }

    public class SupplierResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string TaxId { get; set; } = string.Empty;
        public string BusinessLicenseFilePath { get; set; } = string.Empty;
        public int ApprovalStatus { get; set; }
    }
}
