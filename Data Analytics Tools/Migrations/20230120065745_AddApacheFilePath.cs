using Microsoft.EntityFrameworkCore.Migrations;

namespace Data_Analytics_Tools.Migrations
{
    public partial class AddApacheFilePath : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "ApacheFilesImportProgress",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "ApacheFilesImportProgress");
        }
    }
}
