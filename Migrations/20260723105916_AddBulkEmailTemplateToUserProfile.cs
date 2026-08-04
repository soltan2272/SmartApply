using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobApplicationBot.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkEmailTemplateToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BulkTemplateBody",
                table: "UserProfiles",
                type: "nvarchar(max)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BulkTemplateSubject",
                table: "UserProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BulkTemplateBody",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "BulkTemplateSubject",
                table: "UserProfiles");
        }
    }
}
