using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobApplicationBot.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSLaunchFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchScore",
                table: "JobApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineStatus",
                table: "JobApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Applied");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "BulkEmailDispatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "BulkEmailDispatches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPlan",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Free");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionUpdatedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_User_Pipeline",
                table: "JobApplications",
                columns: new[] { "UserId", "PipelineStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkEmailDispatches_LeaseExpiresAt",
                table: "BulkEmailDispatches",
                column: "LeaseExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobApplications_User_Pipeline",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_BulkEmailDispatches_LeaseExpiresAt",
                table: "BulkEmailDispatches");

            migrationBuilder.DropColumn(
                name: "MatchScore",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "PipelineStatus",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "BulkEmailDispatches");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "BulkEmailDispatches");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SubscriptionUpdatedAt",
                table: "AspNetUsers");
        }
    }
}
