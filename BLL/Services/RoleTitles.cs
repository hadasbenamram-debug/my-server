using DAL.Classes;

namespace BLL.Services
{
    // חדש (תיקון 10): איך קוראים לכל תפקיד בטקסטים למשתמש - "הבקשה שלך להצטרף כמתנדב/ת"
    public static class RoleTitles
    {
        public const string Volunteer = "כמתנדב/ת";
        public const string Supplier = "כספק / תורם";
        public const string Ngo = "כעמותה";

        public static string For(RequestedRole role) => role switch
        {
            RequestedRole.Volunteer => Volunteer,
            RequestedRole.Supplier => Supplier,
            _ => Ngo
        };
    }
}
