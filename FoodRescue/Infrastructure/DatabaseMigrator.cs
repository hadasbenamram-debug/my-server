using DAL;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FoodRescue.Infrastructure
{
    /// <summary>
    /// תיקון 34: בדיקת ה-migrations בעליית השרת.
    ///
    /// למה טבלת DeliveryLogs לא התעדכנה: ה-migration בשם DeliveryLogTracking (העמודות Type ו-MatchId והאינדקס
    /// VolunteerRouteId+Timestamp) לא הורץ על המסד. בלי העמודות האלה כל INSERT ליומן נכשל, והשגיאה נבלעה בשקט
    /// (היומן "לא מכשיל" את הפעולה העסקית) - כך שהאיסוף / המסירה נשמרו, אבל לא נרשם אף אירוע ואף מיקום.
    /// עדכון המיקום עצמו (PUT api/volunteers/me/location) אף החזיר 500, כי הקריאה מהיומן נכשלה.
    ///
    /// עכשיו: אם יש migrations שלא הורצו -
    ///   * Database:AutoMigrate = true (ברירת המחדל בסביבת Development) - הם מורצים אוטומטית לפני שהשרת עולה.
    ///   * אחרת (למשל Production) - נרשמת שגיאה ברורה בלוג עם רשימת ה-migrations והפקודה להרצה.
    /// השרת עולה בכל מקרה (גם אם המסד לא זמין כרגע או שההרצה נכשלה) - כדי שהשגיאה תופיע בלוג ולא תסתיר אחרות.
    /// </summary>
    public static class DatabaseMigrator
    {
        public const string AutoMigrateKey = "Database:AutoMigrate";

        private const string UpdateCommand = "dotnet ef database update --project DAL --startup-project FoodRescue";

        public static async Task EnsureUpToDateAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FoodRescueDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator).FullName!);

            List<string> pending;
            try
            {
                pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not check the database migrations. Is the database reachable (ConnectionStrings:DefaultConnection)?");
                return;
            }

            if (pending.Count == 0)
            {
                logger.LogInformation("Database schema is up to date ({Count} migrations applied).", db.Database.GetMigrations().Count());
                return;
            }

            string list = string.Join(", ", pending);
            bool autoMigrate = app.Configuration.GetValue<bool?>(AutoMigrateKey) ?? app.Environment.IsDevelopment();

            if (!autoMigrate)
            {
                logger.LogError(
                    "The database is missing {Count} migration(s): {Migrations}. Until they are applied, features that depend on them fail " +
                    "(for example the delivery log - DeliveryLogs - stays empty and the live tracking shows nothing). " +
                    "Run: {Command}  (or set {Key}=true to apply them on startup).",
                    pending.Count, list, UpdateCommand, AutoMigrateKey);
                return;
            }

            try
            {
                // מסד שנוצר בלי היסטוריית migrations (למשל EnsureCreated) - Migrate ינסה ליצור טבלאות שכבר קיימות
                bool noHistory = !(await db.Database.GetAppliedMigrationsAsync()).Any();
                if (noHistory && await db.GetService<IRelationalDatabaseCreator>().ExistsAsync() &&
                    await db.GetService<IRelationalDatabaseCreator>().HasTablesAsync())
                {
                    logger.LogError(
                        "The database has tables but no migrations history (__EFMigrationsHistory), so the pending migrations " +
                        "({Migrations}) were not applied automatically. Apply them manually: {Command}", list, UpdateCommand);
                    return;
                }

                logger.LogWarning("Applying {Count} pending database migration(s): {Migrations}", pending.Count, list);
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied. The schema is up to date.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Applying the database migrations ({Migrations}) failed. Apply them manually: {Command}", list, UpdateCommand);
            }
        }
    }
}
