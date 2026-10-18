using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DAL
{
    // Design-time factory to allow EF tools to create the DbContext when the
    // startup project's configuration isn't available at design time.
    // מחרוזת החיבור לא נמצאת באף קובץ בפרויקט: היא נקראת מ-User Secrets (בפיתוח) או ממשתנה הסביבה
    // ConnectionStrings__DefaultConnection - בדיוק כמו בזמן ריצה, כך ש-Add-Migration ו-Update-Database עובדים בלי שינוי.
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FoodRescueDbContext>
    {
        // ה-UserSecretsId של פרויקט ה-API (FoodRescue.csproj). User Secrets שייכים לפרויקט, ולכן משתמשים באותו מזהה.
        private const string ApiUserSecretsId = "9d298646-7cfb-4298-b391-690dcc8dc912";

        public FoodRescueDbContext CreateDbContext(string[] args)
        {
            // Try to locate an appsettings.json by walking up from the current directory.
            var basePath = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6; i++)
            {
                if (File.Exists(Path.Combine(basePath, "appsettings.json")))
                    break;

                var parent = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(parent))
                    break;
                basePath = parent;
            }

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true);

            // אותו סדר כמו ב-ASP.NET Core: User Secrets רק בפיתוח, ומשתני סביבה דורסים הכול
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
                builder.AddUserSecrets(ApiUserSecretsId);

            var configuration = builder
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                  ?? configuration["ConnectionStrings:DefaultConnection"]
                                  ?? configuration["ConnectionString"];

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing. Set it with User Secrets " +
                    "(dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\" --project FoodRescue) " +
                    "or with the environment variable ConnectionStrings__DefaultConnection.");

            var optionsBuilder = new DbContextOptionsBuilder<FoodRescueDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new FoodRescueDbContext(optionsBuilder.Options);
        }
    }
}
