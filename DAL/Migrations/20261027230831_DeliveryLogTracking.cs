using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryLogTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryLogs_VolunteerRouteId",
                table: "DeliveryLogs");

            migrationBuilder.AddColumn<int>(
                name: "MatchId",
                table: "DeliveryLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "DeliveryLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLogs_MatchId",
                table: "DeliveryLogs",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLogs_VolunteerRouteId_Timestamp",
                table: "DeliveryLogs",
                columns: new[] { "VolunteerRouteId", "Timestamp" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryLogs_Matches_MatchId",
                table: "DeliveryLogs",
                column: "MatchId",
                principalTable: "Matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryLogs_Matches_MatchId",
                table: "DeliveryLogs");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryLogs_MatchId",
                table: "DeliveryLogs");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryLogs_VolunteerRouteId_Timestamp",
                table: "DeliveryLogs");

            migrationBuilder.DropColumn(
                name: "MatchId",
                table: "DeliveryLogs");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DeliveryLogs");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLogs_VolunteerRouteId",
                table: "DeliveryLogs",
                column: "VolunteerRouteId");
        }
    }
}
