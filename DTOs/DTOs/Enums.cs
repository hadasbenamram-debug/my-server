namespace FoodRescue.DTOs
{
    // DTO-level enums mirror the domain enums but live in the DTOs project
    // so the DTOs project does not need a reference to the DAL project.
    // תואם ל-DAL.Classes.MatchStatus (אותם שמות ואותם ערכים), כדי שה-DTOs לא יצטרכו הפניה ל-DAL.
    public enum MatchStatus
    {
        AwaitingVolunteerConfirmation,
        Confirmed,
        InPickup,
        InTransit,
        TimedOut,
        Completed,
        Declined,
        NoShow,
        Cancelled
    }

    public enum RequestedRole
    {
        Supplier,
        Ngo,
        Volunteer
    }

    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected
            
    }

}

