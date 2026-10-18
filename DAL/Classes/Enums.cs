namespace DAL.Classes
{
    // שימו לב: הערכים נשמרים במסד כמספרים, ולכן מוסיפים ערכים חדשים רק בסוף.

    public enum RouteStatus
    {
        Planned,      // נבנה ע"י המנוע; ממתין לאישור המתנדב / אושר וטרם יצא לדרך
        InProgress,   // המתנדב יצא לדרך (מעקב מיקום פעיל)
        Completed,    // כל התחנות נמסרו
        Cancelled     // נדחה / פג זמן התגובה / No-Show / בוטל
    }

    public enum RequestedRole
    {
        Supplier,
        Ngo,
        Volunteer
    }

    public enum ApprovalStatus
    {
        Pending,    // ממתין לאישור מנהל
        Approved,   // אושר
        Rejected    // נדחה
    }

    public enum DonationStatus
    {
        Pending,        // ממתינה בערימה, טרם שובצה
        Matched,        // שובצה למתנדב (ממתין לאישורו / אושר)
        PickedUp,       // נאספה מהספק
        Delivered,      // המתנדב סימן מסירה לעמותה
        Completed,      // העמותה אישרה קבלה
        Expired,        // פג תוקף לפני שנאספה
        Cancelled       // בוטלה ע"י הספק או מנהל
    }

    public enum MatchStatus
    {
        AwaitingVolunteerConfirmation, // ממתין לאישור המתנדב
        Confirmed,                     // אושר על ידי המתנדב
        InPickup,                      // המתנדב בדרך לאיסוף
        InTransit,                     // נאסף, בדרך לעמותה
        TimedOut,                      // המתנדב לא הגיב בזמן
        Completed,                     // נמסר לעמותה
        Declined,                      // נדחה על ידי המתנדב
        NoShow,                        // המתנדב לא הגיע לאיסוף
        Cancelled                      // בוטל (התרומה בוטלה / פגה)
    }

    public enum NotificationChannel
    {
        Email,
        Sms,
        InApp
    }

    public enum NotificationStatus
    {
        Pending,
        Sent,
        Failed,
        Sending
    }
}
