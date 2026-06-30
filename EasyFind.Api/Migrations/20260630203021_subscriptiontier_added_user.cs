using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyFind.Api.Migrations
{
    /// <inheritdoc />
    public partial class subscriptiontier_added_user : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionTier",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "AspNetUsers");
        }
    }
}
