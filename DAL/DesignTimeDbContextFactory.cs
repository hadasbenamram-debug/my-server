using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DAL
{
    // Design-time factory to allow EF tools to create the DbContext when the
    // startup project's configuration isn't available at design time.
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FoodRescueDbContext>
    {
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

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                  ?? configuration["ConnectionStrings:DefaultConnection"]
                                  ?? configuration["ConnectionString"];

            if (string.IsNullOrEmpty(connectionString))
                throw new InvalidOperationException("Could not find a connection string named 'DefaultConnection' in any appsettings.json.");

            var optionsBuilder = new DbContextOptionsBuilder<FoodRescueDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new FoodRescueDbContext(optionsBuilder.Options);
        }
    }
}
