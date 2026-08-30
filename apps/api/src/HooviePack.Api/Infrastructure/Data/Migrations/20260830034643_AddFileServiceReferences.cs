using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HooviePack.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileServiceReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StoragePath",
                table: "PostPhotos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "PostPhotos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhotoFileId",
                table: "DogProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AvatarFileId",
                table: "AppUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostPhotos_FileId",
                table: "PostPhotos",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DogProfiles_PhotoFileId",
                table: "DogProfiles",
                column: "PhotoFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_AvatarFileId",
                table: "AppUsers",
                column: "AvatarFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PostPhotos_FileId",
                table: "PostPhotos");

            migrationBuilder.DropIndex(
                name: "IX_DogProfiles_PhotoFileId",
                table: "DogProfiles");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_AvatarFileId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "PostPhotos");

            migrationBuilder.DropColumn(
                name: "PhotoFileId",
                table: "DogProfiles");

            migrationBuilder.DropColumn(
                name: "AvatarFileId",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "StoragePath",
                table: "PostPhotos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
