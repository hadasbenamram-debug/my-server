using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class MatchStatusConcurrencyCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // תיקון 14: Match.Status הוא עכשיו Concurrency Token. זה שינוי במודל בלבד - EF מוסיף בדיקה של הסטטוס
            // המקורי ל-WHERE של כל UPDATE - בלי שינוי בטבלה, ולכן אין כאן פקודות.
            // המיגרציה קיימת כדי שה-Snapshot יתאים למודל (אחרת Update-Database מתלונן על שינויים חסרים).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
