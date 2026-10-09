using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Almuerzo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraAttachmentsJson",
                table: "Outbox",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraAttachmentsJson",
                table: "Outbox");
        }
    }
}
