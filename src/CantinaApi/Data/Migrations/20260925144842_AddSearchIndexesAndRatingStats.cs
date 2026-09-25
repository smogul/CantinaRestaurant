using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CantinaApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexesAndRatingStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<decimal>(
                name: "AverageRating",
                table: "MenuItems",
                type: "numeric(3,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Fills the new stats from existing ratings in one statement, rounding the average to one decimal as the API returns it.
            migrationBuilder.Sql(
                """
                UPDATE "MenuItems" AS m
                SET "RatingCount" = s.rating_count, "AverageRating" = s.average_rating
                FROM (
                    SELECT "MenuItemId", count(*)::int AS rating_count, round(avg("Stars"), 1) AS average_rating
                    FROM "Ratings"
                    GROUP BY "MenuItemId"
                ) AS s
                WHERE m."Id" = s."MenuItemId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Description_Trigram",
                table: "MenuItems",
                column: "Description",
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Name_Id_Live",
                table: "MenuItems",
                columns: new[] { "Name", "Id" },
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Name_Trigram",
                table: "MenuItems",
                column: "Name",
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Description_Trigram",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Name_Id_Live",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Name_Trigram",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "MenuItems");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
