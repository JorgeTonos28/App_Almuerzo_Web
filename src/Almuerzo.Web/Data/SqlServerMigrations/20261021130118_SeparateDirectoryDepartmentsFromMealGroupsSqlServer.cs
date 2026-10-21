using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Almuerzo.Web.Data.SqlServerMigrations
{
    /// <inheritdoc />
    public partial class SeparateDirectoryDepartmentsFromMealGroupsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectoryDepartmentId",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DirectoryDepartments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RegionId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "central"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DirectoryKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DirectoryCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryDepartments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DirectoryDepartmentId",
                table: "Users",
                column: "DirectoryDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryDepartments_RegionId",
                table: "DirectoryDepartments",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryDepartments_RegionId_DirectoryCode",
                table: "DirectoryDepartments",
                columns: new[] { "RegionId", "DirectoryCode" },
                unique: true,
                filter: "DirectoryCode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryDepartments_RegionId_DirectoryKey",
                table: "DirectoryDepartments",
                columns: new[] { "RegionId", "DirectoryKey" },
                unique: true,
                filter: "DirectoryKey IS NOT NULL");

            // Copy only verified NamesV1 data; preserve meal group IDs, memberships, roles and orders.
            migrationBuilder.Sql("""
                INSERT INTO DirectoryDepartments (Id, RegionId, Name, DirectoryKey, DirectoryCode, Status)
                SELECT Id, RegionId, Name, DirectoryKey, DirectoryCode, Status FROM Departments WHERE DirectoryKey IS NOT NULL;
                UPDATE Users SET DirectoryDepartmentId = DepartmentId
                WHERE DirectoryManaged = 1 AND DepartmentId IN (SELECT Id FROM DirectoryDepartments);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_DirectoryDepartments_DirectoryDepartmentId",
                table: "Users",
                column: "DirectoryDepartmentId",
                principalTable: "DirectoryDepartments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_DirectoryDepartments_DirectoryDepartmentId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "DirectoryDepartments");

            migrationBuilder.DropIndex(
                name: "IX_Users_DirectoryDepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DirectoryDepartmentId",
                table: "Users");
        }
    }
}
