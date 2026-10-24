using System;

namespace BLL.Exceptions
{
    // ה-Service לא מחזיר קודי HTTP. כשמשהו לא תקין הוא זורק חריגה שמתארת מה קרה,
    // וה-ExceptionHandlingMiddleware (בשכבת ה-API) מתרגם אותה לקוד HTTP ולהודעה ללקוח:
    //   ArgumentException            -> 400 (בקשה לא תקינה / ולידציה)
    //   UnauthorizedException        -> 401
    //   ForbiddenException           -> 403
    //   NotFoundException            -> 404
    //   ConflictException            -> 409
    //   ServiceUnavailableException  -> 503 (שירות חיצוני, למשל Google Maps, לא זמין)

    // המשתמש המחובר לא נמצא במסד (או שאין לו את הפרופיל הנדרש, למשל מנהל).
    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message) { }
    }

    // הישות המבוקשת לא קיימת (או שאין למשתמש הרשאה לראות שהיא קיימת).
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    // למשתמש אין הרשאה לבצע את הפעולה.
    public class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message) { }
    }

    // הפעולה מתנגשת במצב הנוכחי (כבר קיים, עודכן במקביל וכו').
    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
    }

    // שירות חיצוני שהמערכת תלויה בו (למשל Google Maps) לא זמין, או שהמפתח שלו חסר / נדחה.
    public class ServiceUnavailableException : Exception
    {
        public ServiceUnavailableException(string message) : base(message) { }
    }
}
