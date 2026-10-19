using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Almuerzo.Web.Data.SqlServerMigrations
{
    /// <inheritdoc />
    public partial class DirectoryAliasesAndNamesSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "Regions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DirectoryKey",
                table: "Regions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "Departments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DirectoryKey",
                table: "Departments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_DirectoryKey",
                table: "Regions",
                column: "DirectoryKey",
                unique: true,
                filter: "DirectoryKey IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_RegionId_DirectoryKey",
                table: "Departments",
                columns: new[] { "RegionId", "DirectoryKey" },
                unique: true,
                filter: "DirectoryKey IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Regions_DirectoryKey",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Departments_RegionId_DirectoryKey",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "DirectoryKey",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "DirectoryKey",
                table: "Departments");
        }
    }
}
