using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Data_Analytics_Tools.Migrations
{
    public partial class AddCreateDateColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreateDate",
                table: "ApacheFilesImportProgress",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateDate",
                table: "ApacheFilesImportProgress");
        }
    }
}
