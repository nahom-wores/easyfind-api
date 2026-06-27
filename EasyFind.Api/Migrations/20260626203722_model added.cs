using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyFind.Api.Migrations
{
    /// <inheritdoc />
    public partial class modeladded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TitleAm = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Organization = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DescriptionAm = table.Column<string>(type: "text", nullable: true),
                    ApplyUrl = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobCategory = table.Column<int>(type: "integer", nullable: true),
                    SalaryMin = table.Column<int>(type: "integer", nullable: true),
                    SalaryMax = table.Column<int>(type: "integer", nullable: true),
                    EmploymentType = table.Column<int>(type: "integer", nullable: true),
                    MinExperienceYears = table.Column<int>(type: "integer", nullable: true),
                    ScholarshipField = table.Column<int>(type: "integer", nullable: true),
                    DegreeLevel = table.Column<int>(type: "integer", nullable: true),
                    FundingType = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SeekingType = table.Column<int>(type: "integer", nullable: false),
                    TargetCountries = table.Column<List<string>>(type: "text[]", nullable: false),
                    PreferredJobCategories = table.Column<int[]>(type: "integer[]", nullable: false),
                    PreferredScholarshipFields = table.Column<int[]>(type: "integer[]", nullable: false),
                    TargetDegreeLevel = table.Column<int>(type: "integer", nullable: true),
                    EducationLevel = table.Column<int>(type: "integer", nullable: false),
                    WorkExperienceYears = table.Column<short>(type: "smallint", nullable: false),
                    EnglishLevel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CvFileUrl = table.Column<string>(type: "text", nullable: true),
                    CvUploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Deadline",
                table: "Listings",
                column: "Deadline");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsFeatured",
                table: "Listings",
                column: "IsFeatured");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_JobCategory",
                table: "Listings",
                column: "JobCategory");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_ScholarshipField",
                table: "Listings",
                column: "ScholarshipField");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Type_CountryCode_IsActive",
                table: "Listings",
                columns: new[] { "Type", "CountryCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
