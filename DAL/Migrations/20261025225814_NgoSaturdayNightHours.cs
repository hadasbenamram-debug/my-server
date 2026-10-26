using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class NgoSaturdayNightHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReceivesOnSaturdayNight",
                table: "Ngos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SaturdayNightCloseTime",
                table: "Ngos",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SaturdayNightOpenTime",
                table: "Ngos",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivesOnSaturdayNight",
                table: "Ngos");

            migrationBuilder.DropColumn(
                name: "SaturdayNightCloseTime",
                table: "Ngos");

            migrationBuilder.DropColumn(
                name: "SaturdayNightOpenTime",
                table: "Ngos");
        }
    }
}
