namespace FoodRescue.DTOs
{
    // DTO-level enums mirror the domain enums but live in the DTOs project
    // so the DTOs project does not need a reference to the DAL project.
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

