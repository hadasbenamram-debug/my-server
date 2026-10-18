using Hangfire.Dashboard;

namespace FoodRescue.Security
{
    /// <summary>
    /// Hangfire dashboard is reachable only by an authenticated Admin (JWT bearer), in every environment.
    /// By default Hangfire allows only local requests and, in Development, many setups open it to everyone.
    /// </summary>
    public sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var user = context.GetHttpContext().User;

            return user?.Identity?.IsAuthenticated == true &&
                   user.IsInRole("Admin");
        }
    }
}
