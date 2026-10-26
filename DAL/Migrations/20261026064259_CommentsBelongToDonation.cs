using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class CommentsBelongToDonation : Migration
    {
        // תיקון 18: כל תגובה שייכת לתרומה - DonationId חובה, והעמודה MatchId יורדת.
        // לפני שינוי העמודות, הנתונים הקיימים מתעדכנים:
        // 1. תגובה על שיבוץ מקבלת את התרומה של השיבוץ.
        // 2. תשובה בלי תרומה מקבלת את התרומה של התגובה שאליה ענו (לכל עומק).
        // 3. תגובות שגם אחרי זה לא שייכות לאף תרומה ("תגובות סתם" מלפני שהשרת חייב יעד) - נמחקות.
        //    תשובה ששייכת לתרומה ונמצאת מתחת לתגובה כזו נשארת, כתגובה ראשית.
        // 4. תשובה שהתרומה שלה שונה מהתרומה של התגובה שאליה ענו (נתונים ישנים לא עקביים) - הופכת לתגובה ראשית.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE c SET c.DonationId = m.DonationId
FROM Comments AS c
INNER JOIN Matches AS m ON m.Id = c.MatchId
WHERE c.DonationId IS NULL;");

            migrationBuilder.Sql(@"
WHILE 1 = 1
BEGIN
    UPDATE child SET child.DonationId = parent.DonationId
    FROM Comments AS child
    INNER JOIN Comments AS parent ON parent.Id = child.ParentCommentId
    WHERE child.DonationId IS NULL AND parent.DonationId IS NOT NULL;

    IF @@ROWCOUNT = 0 BREAK;
END");

            migrationBuilder.Sql(@"
UPDATE Comments SET ParentCommentId = NULL
WHERE ParentCommentId IN (SELECT Id FROM Comments WHERE DonationId IS NULL);

DELETE FROM Comments WHERE DonationId IS NULL;");

            migrationBuilder.Sql(@"
UPDATE child SET child.ParentCommentId = NULL
FROM Comments AS child
INNER JOIN Comments AS parent ON parent.Id = child.ParentCommentId
WHERE child.DonationId <> parent.DonationId;");

            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Donations_DonationId",
                table: "Comments");

            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Matches_MatchId",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_MatchId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "MatchId",
                table: "Comments");

            migrationBuilder.AlterColumn<int>(
                name: "DonationId",
                table: "Comments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Donations_DonationId",
                table: "Comments",
                column: "DonationId",
                principalTable: "Donations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Donations_DonationId",
                table: "Comments");

            migrationBuilder.AlterColumn<int>(
                name: "DonationId",
                table: "Comments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "MatchId",
                table: "Comments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_MatchId",
                table: "Comments",
                column: "MatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Donations_DonationId",
                table: "Comments",
                column: "DonationId",
                principalTable: "Donations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Matches_MatchId",
                table: "Comments",
                column: "MatchId",
                principalTable: "Matches",
                principalColumn: "Id");
        }
    }
}
