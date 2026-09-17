using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HooviePack.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberBirthDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "AppUsers",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "AppUsers");
        }
    }
}
