using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <summary>
    /// תיקון 75: "למה המנוע החליט כך" - עמודה להסבר ההחלטה של כל מסלול (JSON).
    /// ריקה במסלולים שנוצרו לפני התיקון.
    /// </summary>
    public partial class RouteDecisionExplanation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionJson",
                table: "VolunteerRoutes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionJson",
                table: "VolunteerRoutes");
        }
    }
}
