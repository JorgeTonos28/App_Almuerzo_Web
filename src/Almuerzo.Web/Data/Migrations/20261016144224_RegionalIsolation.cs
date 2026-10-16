using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Almuerzo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegionalIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SourceRecords_Sheet_Row",
                table: "SourceRecords");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_Kind_Email_Scope",
                table: "Ratings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Holidays",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Name",
                table: "Categories");

            migrationBuilder.AddColumn<bool>(
                name: "DirectoryManaged",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Users",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "SourceRecords",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Settings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Ratings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "RatingHistory",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<long>(
                name: "NextAttemptUtcTicks",
                table: "Outbox",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Outbox",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Orders",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Menu",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "JobRuns",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Imports",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Holidays",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "DirectoryCode",
                table: "Departments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Departments",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Categories",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "Audit",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "central");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                columns: new[] { "RegionId", "Key" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Holidays",
                table: "Holidays",
                columns: new[] { "RegionId", "Date" });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    DirectoryCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_RegionId",
                table: "Users",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRecords_RegionId",
                table: "SourceRecords",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRecords_RegionId_Sheet_Row",
                table: "SourceRecords",
                columns: new[] { "RegionId", "Sheet", "Row" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_RegionId",
                table: "Settings",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_RegionId",
                table: "Ratings",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_RegionId_Kind_Email_Scope",
                table: "Ratings",
                columns: new[] { "RegionId", "Kind", "Email", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RatingHistory_RegionId",
                table: "RatingHistory",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_RegionId",
                table: "Outbox",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RegionId",
                table: "Orders",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Menu_RegionId",
                table: "Menu",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_RegionId",
                table: "JobRuns",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Imports_RegionId",
                table: "Imports",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_RegionId",
                table: "Holidays",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_RegionId",
                table: "Departments",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_RegionId_DirectoryCode",
                table: "Departments",
                columns: new[] { "RegionId", "DirectoryCode" },
                unique: true,
                filter: "DirectoryCode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_RegionId_Name",
                table: "Departments",
                columns: new[] { "RegionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_RegionId",
                table: "Categories",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_RegionId_Name",
                table: "Categories",
                columns: new[] { "RegionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Audit_RegionId",
                table: "Audit",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_DirectoryCode",
                table: "Regions",
                column: "DirectoryCode",
                unique: true,
                filter: "DirectoryCode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Users_RegionId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_SourceRecords_RegionId",
                table: "SourceRecords");

            migrationBuilder.DropIndex(
                name: "IX_SourceRecords_RegionId_Sheet_Row",
                table: "SourceRecords");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_RegionId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_RegionId",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_RegionId_Kind_Email_Scope",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_RatingHistory_RegionId",
                table: "RatingHistory");

            migrationBuilder.DropIndex(
                name: "IX_Outbox_RegionId",
                table: "Outbox");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RegionId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Menu_RegionId",
                table: "Menu");

            migrationBuilder.DropIndex(
                name: "IX_JobRuns_RegionId",
                table: "JobRuns");

            migrationBuilder.DropIndex(
                name: "IX_Imports_RegionId",
                table: "Imports");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Holidays",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_Holidays_RegionId",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_Departments_RegionId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_RegionId_DirectoryCode",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_RegionId_Name",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_RegionId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_RegionId_Name",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Audit_RegionId",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "DirectoryManaged",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "SourceRecords");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "RatingHistory");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtcTicks",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Menu");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "JobRuns");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Imports");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Holidays");

            migrationBuilder.DropColumn(
                name: "DirectoryCode",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Audit");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Holidays",
                table: "Holidays",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRecords_Sheet_Row",
                table: "SourceRecords",
                columns: new[] { "Sheet", "Row" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_Kind_Email_Scope",
                table: "Ratings",
                columns: new[] { "Kind", "Email", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);
        }
    }
}
