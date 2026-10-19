using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Almuerzo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistentDirectoryOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DirectoryManaged",
                table: "Regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DirectoryManaged",
                table: "Departments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DirectoryManaged",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "DirectoryManaged",
                table: "Departments");
        }
    }
}
