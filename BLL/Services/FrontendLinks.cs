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
    }
}
