using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HooviePack.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDogipediaCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DogipediaBreedGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogipediaBreedGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DogipediaSyncStates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulBreedCount = table.Column<int>(type: "integer", nullable: false),
                    LastSuccessfulGroupCount = table.Column<int>(type: "integer", nullable: false),
                    LastSuccessfulImageCount = table.Column<int>(type: "integer", nullable: false),
                    LastFailureAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogipediaSyncStates", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "DogipediaBreeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalBreedId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Hypoallergenic = table.Column<bool>(type: "boolean", nullable: true),
                    LifeMinYears = table.Column<decimal>(type: "numeric", nullable: true),
                    LifeMaxYears = table.Column<decimal>(type: "numeric", nullable: true),
                    MaleWeightMinKg = table.Column<decimal>(type: "numeric", nullable: true),
                    MaleWeightMaxKg = table.Column<decimal>(type: "numeric", nullable: true),
                    FemaleWeightMinKg = table.Column<decimal>(type: "numeric", nullable: true),
                    FemaleWeightMaxKg = table.Column<decimal>(type: "numeric", nullable: true),
                    MaleHeightMinCm = table.Column<decimal>(type: "numeric", nullable: true),
                    MaleHeightMaxCm = table.Column<decimal>(type: "numeric", nullable: true),
                    FemaleHeightMinCm = table.Column<decimal>(type: "numeric", nullable: true),
                    FemaleHeightMaxCm = table.Column<decimal>(type: "numeric", nullable: true),
                    OriginCountry = table.Column<string>(type: "text", nullable: true),
                    OriginRegion = table.Column<string>(type: "text", nullable: true),
                    OriginEra = table.Column<string>(type: "text", nullable: true),
                    CoatType = table.Column<string>(type: "text", nullable: true),
                    CoatLength = table.Column<string>(type: "text", nullable: true),
                    CoatColors = table.Column<string[]>(type: "text[]", nullable: false),
                    Temperament = table.Column<string[]>(type: "text[]", nullable: false),
                    OtherNames = table.Column<string[]>(type: "text[]", nullable: false),
                    RecognizedBy = table.Column<string[]>(type: "text[]", nullable: false),
                    Sources = table.Column<string>(type: "jsonb", nullable: false),
                    Energy = table.Column<int>(type: "integer", nullable: true),
                    Trainability = table.Column<int>(type: "integer", nullable: true),
                    Barking = table.Column<int>(type: "integer", nullable: true),
                    Grooming = table.Column<int>(type: "integer", nullable: true),
                    Shedding = table.Column<int>(type: "integer", nullable: true),
                    Drooling = table.Column<int>(type: "integer", nullable: true),
                    GoodWithChildren = table.Column<int>(type: "integer", nullable: true),
                    GoodWithDogs = table.Column<int>(type: "integer", nullable: true),
                    GoodWithStrangers = table.Column<int>(type: "integer", nullable: true),
                    ApartmentFriendly = table.Column<int>(type: "integer", nullable: true),
                    ExerciseMinutes = table.Column<int>(type: "integer", nullable: true),
                    BreedGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogipediaBreeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DogipediaBreeds_DogipediaBreedGroups_BreedGroupId",
                        column: x => x.BreedGroupId,
                        principalTable: "DogipediaBreedGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DogipediaBreedImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalImageId = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalUrl = table.Column<string>(type: "text", nullable: true),
                    ThumbUrl = table.Column<string>(type: "text", nullable: true),
                    MediumUrl = table.Column<string>(type: "text", nullable: true),
                    LargeUrl = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: true),
                    License = table.Column<string>(type: "text", nullable: true),
                    LicenseUrl = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogipediaBreedImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DogipediaBreedImages_DogipediaBreeds_BreedId",
                        column: x => x.BreedId,
                        principalTable: "DogipediaBreeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreedGroups_ExternalGroupId",
                table: "DogipediaBreedGroups",
                column: "ExternalGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreedImages_BreedId",
                table: "DogipediaBreedImages",
                column: "BreedId");

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreedImages_ExternalImageId",
                table: "DogipediaBreedImages",
                column: "ExternalImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreeds_BreedGroupId",
                table: "DogipediaBreeds",
                column: "BreedGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreeds_ExternalBreedId",
                table: "DogipediaBreeds",
                column: "ExternalBreedId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreeds_IsActive",
                table: "DogipediaBreeds",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DogipediaBreeds_Name",
                table: "DogipediaBreeds",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DogipediaBreedImages");

            migrationBuilder.DropTable(
                name: "DogipediaSyncStates");

            migrationBuilder.DropTable(
                name: "DogipediaBreeds");

            migrationBuilder.DropTable(
                name: "DogipediaBreedGroups");
        }
    }
}
