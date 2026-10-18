namespace FoodRescue.DTOs
{
    // מצב ההתחברות העדכני של משתמש, כפי שה-Service מחזיר לשכבת ה-API בכל בקשה מאומתת.
    // התפקיד נלקח מהמסד ולא מהטוקן, כך שאישור/דחייה של מנהל משפיעים מיד.
    public class SessionStateDto
    {
        public string Role { get; set; } = string.Empty;
        public bool MustChangePassword { get; set; }
    }
}
