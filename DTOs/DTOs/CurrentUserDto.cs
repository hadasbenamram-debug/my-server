namespace FoodRescue.DTOs
{
    // מי המשתמש שמבצע את הבקשה.
    // הקונטרולר בונה אותו מתוך ה-JWT (לעולם לא מנתונים שהלקוח שלח) ומעביר אותו ל-Service,
    // וה-Service מחליט לפיו על הרשאות (בעלות על תרומה, השתתפות בשידוך וכו').
    public class CurrentUserDto
    {
        public int? UserId { get; set; }
        public bool IsAdmin { get; set; }
    }
}
