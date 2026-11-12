using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 34: יומן המשלוח (DeliveryLogs) לא התעדכן כשה-migration בשם DeliveryLogTracking לא הורץ על המסד:
    // חסרות העמודות Type ו-MatchId, וכל INSERT / SELECT לטבלה נכשל (והשגיאה נבלעה).
    // עכשיו השרת בודק בעלייה אם יש migrations שלא הורצו (DatabaseMigrator). הבדיקות כאן מוודאות שה-migrations
    // מכסים את כל המודל - אחרת Migrate() נכשל ב-EF Core 10 (PendingModelChangesWarning), והמסד לא יכול להתעדכן.
    public class DatabaseMigrationsTests
    {
        private static FoodRescueDbContext SqlServerModel() => new(
            new DbContextOptionsBuilder<FoodRescueDbContext>()
                // לא נפתח חיבור: רק המודל וה-migrations נבדקים
                .UseSqlServer("Server=(local);Database=FoodRescueModelCheck;Trusted_Connection=True;TrustServerCertificate=True")
                .Options);

        [Fact]
        public void TheMigrationsCoverTheWholeModel()
        {
            using var db = SqlServerModel();

            Assert.False(db.Database.HasPendingModelChanges(),
                "יש שינויים במודל שלא נכנסו ל-migration. יש להריץ: dotnet ef migrations add <שם> --project DAL --startup-project FoodRescue");
        }

        [Fact]
        public void TheDeliveryLogTrackingMigrationIsPartOfTheProject()
        {
            using var db = SqlServerModel();

            Assert.Contains(db.Database.GetMigrations(), m => m.EndsWith("_DeliveryLogTracking"));
            var deliveryLog = db.Model.FindEntityType(typeof(DeliveryLog))!;
            Assert.NotNull(deliveryLog.FindProperty(nameof(DeliveryLog.Type)));
            Assert.NotNull(deliveryLog.FindProperty(nameof(DeliveryLog.MatchId)));
            Assert.Contains(deliveryLog.GetIndexes(), i =>
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(DeliveryLog.VolunteerRouteId), nameof(DeliveryLog.Timestamp) }));
        }

        // תיקון 43: העמודה "זמינה לאיסוף החל מ-" - עמודת זמן שאפשר להשאיר ריקה (תרומות קיימות = אפשר לאסוף מיד)
        [Fact]
        public void TheEarliestPickupTimeMigrationIsPartOfTheProject()
        {
            using var db = SqlServerModel();

            Assert.Contains(db.Database.GetMigrations(), m => m.EndsWith("_DonationEarliestPickupTime"));
            var column = db.Model.FindEntityType(typeof(Donation))!.FindProperty(nameof(Donation.EarliestPickupTime));
            Assert.NotNull(column);
            Assert.True(column!.IsNullable);
        }
    }
}
