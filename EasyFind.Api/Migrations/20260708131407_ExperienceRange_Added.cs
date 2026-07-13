using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyFind.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExperienceRange_Added : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExperienceRange",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExperienceRange",
                table: "UserProfiles");
        }
    }
}
