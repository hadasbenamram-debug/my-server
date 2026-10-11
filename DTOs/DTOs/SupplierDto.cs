using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת ספק חדש (ללא מזהה שנקבע בצורה מאובטחת בשרת)
    public class CreateSupplierDto
    {
        [Required(ErrorMessage = "שם העסק הוא שדה חובה.")]
        [StringLength(150, ErrorMessage = "שם העסק יכול להכיל עד 150 תווים.")]
        public string BusinessName { get; set; }

        [Required(ErrorMessage = "כתובת העסק היא שדה חובה.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; }

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [StringLength(50, ErrorMessage = "מספר ח.פ. / עוסק מורשה יכול להכיל עד 50 תווים.")]
        public string TaxId { get; set; }

        // שדה חובה בישות Supplier — בלעדיו השמירה למסד נכשלת
        [Required(ErrorMessage = "רישיון עסק הוא שדה חובה.")]
        public string BusinessLicenseFilePath { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class SupplierResponseDto
    {
        public int Id { get; set; }
        public string BusinessName { get; set; }
        public string Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string TaxId { get; set; }
        public int ApprovalStatus { get; set; }
    }
}