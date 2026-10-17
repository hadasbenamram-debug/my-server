using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowSecurityAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Donations_Regions_RegionId",
                table: "Donations");

            migrationBuilder.DropForeignKey(
                name: "FK_Ngos_Regions_RegionId",
                table: "Ngos");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Matches_VolunteerId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleDirectionsPolyline",
                table: "VolunteerRoutes",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "VolunteerRoutes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EstimatedTotalDistanceKm",
                table: "VolunteerRoutes",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MatchScore",
                table: "VolunteerRoutes",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "VolunteerRoutes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // ===== נתונים קיימים: משתמשים =====
            // חותמת אבטחה אקראית לכל משתמש (הטוקנים הקיימים יפסיקו לעבוד - יש להתחבר מחדש פעם אחת)
            migrationBuilder.Sql(@"
UPDATE [Users] SET [SecurityStamp] = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'') WHERE [SecurityStamp] = N'';");

            // תוקן: עמותות שאושרו קיבלו Role=User. מעדכנים את התפקיד לפי הפרופיל הקיים.
            migrationBuilder.Sql(@"
UPDATE [Users] SET [Role] = 4 WHERE [Role] = 0 AND [Id] IN (SELECT [UserId] FROM [Ngos]);
UPDATE [Users] SET [Role] = 2 WHERE [Role] = 0 AND [Id] IN (SELECT [UserId] FROM [Suppliers]);
UPDATE [Users] SET [Role] = 3 WHERE [Role] = 0 AND [Id] IN (SELECT [UserId] FROM [Volunteers]);");

            // תוקן: דחיית עמותה לא עדכנה את בקשת האישור - מסנכרנים בקשות שנשארו Pending
            migrationBuilder.Sql(@"
UPDATE ar SET ar.[Status] = 2, ar.[HandledAt] = SYSUTCDATETIME()
FROM [ApprovalRequests] ar
JOIN [Ngos] n ON n.[UserId] = ar.[UserId]
WHERE ar.[RequestedRole] = 1 AND ar.[Status] = 0 AND n.[ApprovalStatus] = 2;");

            // ===== נתונים קיימים: התראות =====
            // הגוף של מיילים ישנים נשמר כטקסט גלוי (כולל סיסמאות זמניות וקודי אימות) - נמחק.
            // הנושא והשגיאה מקוצרים לאורך החדש כדי ששינוי סוג העמודה לא ייכשל.
            migrationBuilder.Sql(@"
UPDATE [Notifications]
SET [Content] = N'',
    [Subject] = LEFT([Subject], 200),
    [ErrorMessage] = LEFT([ErrorMessage], 500);");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Notifications",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "Notifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalMessageId",
                table: "Notifications",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "Notifications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Notifications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link",
                table: "Notifications",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPayload",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Recipient",
                table: "Notifications",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE [Notifications] SET [Content] = NULL WHERE [Content] = N'';
UPDATE [Notifications] SET [ErrorMessage] = NULL WHERE [ErrorMessage] = N'';
UPDATE [Notifications] SET [ExternalMessageId] = NULL WHERE [ExternalMessageId] = N'';
UPDATE [Notifications]
SET [Status] = 2, -- Failed
    [ErrorMessage] = N'Not re-sent after the outbox migration (the old body was not stored securely).'
WHERE [Status] = 0 AND [ProtectedPayload] IS NULL;");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Matches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PickedUpAt",
                table: "Matches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "Matches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "Donations",
                type: "datetime2",
                nullable: true);

            // ===== נתונים קיימים: שידוכים ותרומות =====
            // תוקן: ResponseDeadline לא נקבע אף פעם, ולכן שידוכים ממתינים לא פגו. קובעים לפי זמן השיבוץ + 5 דקות,
            // וג'וב ה-timeout יטפל בהם בריצה הראשונה.
            migrationBuilder.Sql(@"
UPDATE [Matches] SET [ResponseDeadline] = DATEADD(minute, 5, [AssignedAt])
WHERE [ResponseDeadline] IS NULL AND [Status] = 0 AND [IsActive] = 1;");

            // תוקן: CreatedAt של תרומות נשמר כ-UtcNow+3h. מחזירים ל-UTC אמיתי.
            migrationBuilder.Sql(@"UPDATE [Donations] SET [CreatedAt] = DATEADD(hour, -3, [CreatedAt]);");

            // תרומות שתאריך התפוגה שלהן עבר ועדיין ממתינות - מסומנות Expired (מעכשיו ג'וב ExpireDonations עושה זאת)
            migrationBuilder.Sql(@"UPDATE [Donations] SET [Status] = 5 WHERE [Status] = 0 AND [ExpirationTime] < SYSUTCDATETIME();");

            migrationBuilder.CreateTable(
                name: "ActionTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: true),
                    TargetValue = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_NextAttemptAt",
                table: "Notifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Channel_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "Channel", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status_ResponseDeadline",
                table: "Matches",
                columns: new[] { "Status", "ResponseDeadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_VolunteerId_IsActive",
                table: "Matches",
                columns: new[] { "VolunteerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTokens_Purpose_TokenHash",
                table: "ActionTokens",
                columns: new[] { "Purpose", "TokenHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTokens_UserId_Purpose_CreatedAt",
                table: "ActionTokens",
                columns: new[] { "UserId", "Purpose", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Donations_Regions_RegionId",
                table: "Donations",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ngos_Regions_RegionId",
                table: "Ngos",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // הערה: תיקוני הנתונים (חותמות, תפקידים, זמני יצירה) לא מבוטלים ב-Down.
            migrationBuilder.Sql(@"UPDATE [Donations] SET [CreatedAt] = DATEADD(hour, 3, [CreatedAt]);");

            migrationBuilder.DropForeignKey(
                name: "FK_Donations_Regions_RegionId",
                table: "Donations");

            migrationBuilder.DropForeignKey(
                name: "FK_Ngos_Regions_RegionId",
                table: "Ngos");

            migrationBuilder.DropTable(
                name: "ActionTokens");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Status_NextAttemptAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Channel_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Matches_Status_ResponseDeadline",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_VolunteerId_IsActive",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "VolunteerRoutes");

            migrationBuilder.DropColumn(
                name: "EstimatedTotalDistanceKm",
                table: "VolunteerRoutes");

            migrationBuilder.DropColumn(
                name: "MatchScore",
                table: "VolunteerRoutes");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "VolunteerRoutes");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Link",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ProtectedPayload",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Recipient",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PickedUpAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "Donations");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleDirectionsPolyline",
                table: "VolunteerRoutes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEnd",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalMessageId",
                table: "Notifications",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_VolunteerId",
                table: "Matches",
                column: "VolunteerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Donations_Regions_RegionId",
                table: "Donations",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Ngos_Regions_RegionId",
                table: "Ngos",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
