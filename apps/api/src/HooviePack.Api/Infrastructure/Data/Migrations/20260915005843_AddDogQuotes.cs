using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HooviePack.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDogQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DogQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Work = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogQuotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DogQuotes_Author",
                table: "DogQuotes",
                column: "Author");

            migrationBuilder.CreateIndex(
                name: "IX_DogQuotes_Category",
                table: "DogQuotes",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_DogQuotes_IsActive",
                table: "DogQuotes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DogQuotes_IsActive_Category",
                table: "DogQuotes",
                columns: new[] { "IsActive", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DogQuotes");
        }
    }
}
