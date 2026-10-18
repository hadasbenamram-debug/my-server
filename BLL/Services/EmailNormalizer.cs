namespace BLL.Services
{
    /// <summary>One definition of "normalized e-mail" used by registration, login, forgot-password and lookups.</summary>
    public static class EmailNormalizer
    {
        public static string Normalize(string? email) =>
            (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
