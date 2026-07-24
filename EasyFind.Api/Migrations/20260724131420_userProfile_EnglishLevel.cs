using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyFind.Api.Migrations
{
    /// <inheritdoc />
    public partial class userProfile_EnglishLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnglishTestScore",
                table: "UserProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnglishTestType",
                table: "UserProfiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnglishTestScore",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "EnglishTestType",
                table: "UserProfiles");
        }
    }
}
