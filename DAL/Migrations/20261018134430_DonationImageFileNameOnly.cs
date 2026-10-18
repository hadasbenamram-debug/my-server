using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class DonationImageFileNameOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // לפי מדריך התמונות: במסד נשמר רק שם הקובץ (למשל a1b2c3.jpg), בלי נתיב ובלי כתובת.
            // 1. שינוי שם העמודה בלי לאבד נתונים (EF היה מוחק ויוצר עמודה חדשה)
            migrationBuilder.RenameColumn(
                name: "ImageFilePath",
                table: "Donations",
                newName: "Image");

            // 2. תיקון הנתונים הקיימים: הסרת הקידומת "/images/" (או כל נתיב אחר) והשארת שם הקובץ בלבד.
            //    ערך ריק הופך ל-NULL ("אין תמונה").
            //    (בתוך EXEC, כדי שגם סקריפט SQL שרץ כ-batch אחד יכיר את העמודה אחרי שינוי השם)
            migrationBuilder.Sql(@"EXEC(N'
UPDATE Donations
SET Image = RIGHT(REPLACE(Image, ''\'', ''/''), CHARINDEX(''/'', REVERSE(REPLACE(Image, ''\'', ''/''))) - 1)
WHERE Image IS NOT NULL AND CHARINDEX(''/'', REPLACE(Image, ''\'', ''/'')) > 0;

UPDATE Donations SET Image = NULL WHERE Image IS NOT NULL AND LTRIM(RTRIM(Image)) = '''';
UPDATE Donations SET Image = NULL WHERE Image IS NOT NULL AND LEN(Image) > 100;
')");

            // 3. שם קובץ קצר: nvarchar(100) במקום nvarchar(max)
            migrationBuilder.AlterColumn<string>(
                name: "Image",
                table: "Donations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Image",
                table: "Donations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.Sql(@"UPDATE Donations SET Image = '/images/' + Image WHERE Image IS NOT NULL AND Image <> '';");

            migrationBuilder.RenameColumn(
                name: "Image",
                table: "Donations",
                newName: "ImageFilePath");
        }
    }
}
