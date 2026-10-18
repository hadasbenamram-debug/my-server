using FoodRescue.DTOs.Validation;
using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // שדות המשתמש המשותפים לכל טפסי ההרשמה. האורכים תואמים בדיוק את המסד (User):
    // Name עד 100, Phone עד 20. בלי הבדיקות האלה ערך ארוך מדי נכשל רק ב-INSERT (500 במקום 400).
    public abstract class RegisterUserFieldsDto
    {
        [Required(ErrorMessage = "יש להזין שם מלא.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "השם חייב להכיל בין 2 ל-100 תווים.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין כתובת אימייל.")]
        [EmailAddress(ErrorMessage = "כתובת האימייל אינה תקינה.")]
        [StringLength(254, ErrorMessage = "כתובת האימייל ארוכה מדי.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין מספר טלפון.")]
        [Phone(ErrorMessage = "מספר הטלפון אינו תקין.")]
        [StringLength(20, ErrorMessage = "מספר הטלפון יכול להכיל עד 20 תווים.")]
        public string Phone { get; set; } = string.Empty;
    }

    // נתוני הרשמה של ספק + יצירת משתמש (POST api/Suppliers/register, multipart/form-data).
    public class RegisterSupplierDto : RegisterUserFieldsDto
    {
        [Required(ErrorMessage = "יש להזין את שם העסק.")]
        [StringLength(100, ErrorMessage = "שם העסק יכול להכיל עד 100 תווים.")]
        public string BusinessName { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין את כתובת העסק.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [Required(ErrorMessage = "יש להזין ח.פ / מספר עוסק.")]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "ח.פ / מספר עוסק יכול להכיל 5-20 תווים.")]
        public string TaxId { get; set; } = string.Empty;

        // נשלח כ-multipart/form-data. השרת שומר את הקובץ וקובע את שמו.
        [Required(ErrorMessage = "חובה לצרף קובץ רישיון עסק.")]
        public Microsoft.AspNetCore.Http.IFormFile LicenseFile { get; set; } = null!;
    }

    // נתוני הרשמה של עמותה + יצירת משתמש (POST api/Ngos/register).
    public class RegisterNgoDto : RegisterUserFieldsDto
    {
        [Required(ErrorMessage = "יש להזין את שם העמותה.")]
        [StringLength(100, ErrorMessage = "שם העמותה יכול להכיל עד 100 תווים.")]
        public string NgoName { get; set; } = string.Empty;

        [Range(1, 100000, ErrorMessage = "הקיבולת היומית חייבת להיות מספר חיובי.")]
        public int DailyCapacity { get; set; }

        [Required(ErrorMessage = "יש להזין את כתובת העמותה.")]
        [StringLength(200, ErrorMessage = "הכתובת יכולה להכיל עד 200 תווים.")]
        public string Address { get; set; } = string.Empty;

        [Range(-90, 90, ErrorMessage = "קו רוחב חייב להיות בין -90 ל-90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "קו אורך חייב להיות בין -180 ל-180.")]
        public double Longitude { get; set; }

        [Required(ErrorMessage = "יש להזין מספר רישום (עמותה / חברה).")]
        [StringLength(20, ErrorMessage = "מספר הרישום יכול להכיל עד 20 תווים.")]
        public string RegistrationNumber { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור אזור.")]
        public int RegionId { get; set; }

        // שעות קבלה בימים א'-ה'. לא חובה בהרשמה (ברירת מחדל 09:00-17:00), אפשר לעדכן אחרי האישור.
        public TimeSpan? SunToThuOpenTime { get; set; }
        public TimeSpan? SunToThuCloseTime { get; set; }

        public bool ReceivesOnFriday { get; set; }
        public TimeSpan? FridayOpenTime { get; set; }
        public TimeSpan? FridayCloseTime { get; set; }
    }

    // נתוני הרשמה של מתנדב + יצירת משתמש (POST api/Volunteers/register).
    public class RegisterVolunteerDto : RegisterUserFieldsDto
    {
        [Required(ErrorMessage = "יש להזין סוג רכב.")]
        [StringLength(50, ErrorMessage = "סוג הרכב יכול להכיל עד 50 תווים.")]
        public string VehicleType { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.1", "10000", ErrorMessage = "קיבולת הרכב חייבת להיות בין 0.1 ל-10,000 ק\"ג.")]
        public decimal MaxCapacityKg { get; set; }

        [Required(ErrorMessage = "יש להזין מספר תעודת זהות.")]
        [StringLength(15, ErrorMessage = "מספר תעודת הזהות ארוך מדי.")]
        [IsraeliId]
        public string IdNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "יש להזין מספר רישיון נהיגה.")]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "מספר רישיון הנהיגה חייב להכיל 5-20 תווים.")]
        public string DriverLicenseNumber { get; set; } = string.Empty;

        // חדש: תוקף רישיון הנהיגה נבדק (רישיון שפג לא מתקבל, והמנוע לא משבץ מתנדב שהרישיון שלו פג)
        [Required(ErrorMessage = "יש להזין את תאריך תוקף רישיון הנהיגה.")]
        [NotInPast(ErrorMessage = "תוקף רישיון הנהיגה פג.")]
        public DateTime? DriverLicenseExpiry { get; set; }

        [Required(ErrorMessage = "יש להזין תאריך לידה.")]
        [MinimumAge(17, ErrorMessage = "מתנדב נהג חייב להיות בן 17 לפחות.")]
        public DateTime DateOfBirth { get; set; }
    }
}
