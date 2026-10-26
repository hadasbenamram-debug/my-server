using Microsoft.Extensions.Configuration;
using System;

namespace BLL.Services
{
    /// <summary>Builds absolute links into the React client from App:FrontendBaseUrl (no hard-coded host).</summary>
    public static class FrontendLinks
    {
        public static string Build(IConfiguration configuration, string pathAndQuery) =>
            TryBuild(configuration, pathAndQuery)
            ?? throw new InvalidOperationException("App:FrontendBaseUrl is not configured.");

        public static string? TryBuild(IConfiguration configuration, string pathAndQuery)
        {
            var baseUrl = configuration["App:FrontendBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            return $"{baseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";
        }

        // המסך הראשון של כל תפקיד אחרי האישור (גם בהתראה וגם בקישור הכניסה מהמייל)
        public static string RoleHomePath(DAL.Classes.RequestedRole role) => role switch
        {
            DAL.Classes.RequestedRole.Volunteer => "/app/volunteer/availability",
            DAL.Classes.RequestedRole.Supplier => "/app/supplier/dashboard",
            _ => "/app/ngo/hours"
        };
    }
}
