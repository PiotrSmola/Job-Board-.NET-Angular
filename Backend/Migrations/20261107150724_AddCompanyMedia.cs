using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfferBackgroundContentType",
                table: "Companies",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // istniejące firmy dostają domyślne przyciemnienie tła (75%) - wartość domyślna tylko w migracji, nie w modelu
            migrationBuilder.AddColumn<int>(
                name: "OfferBackgroundDim",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<int>(
                name: "OfferBackgroundFocus",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OfferBackgroundKey",
                table: "Companies",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompanyPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FileKey = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Caption = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyPhotos_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPhotos_CompanyId_SortOrder",
                table: "CompanyPhotos",
                columns: new[] { "CompanyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPhotos_FileKey",
                table: "CompanyPhotos",
                column: "FileKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyPhotos");

            migrationBuilder.DropColumn(
                name: "OfferBackgroundContentType",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OfferBackgroundDim",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OfferBackgroundFocus",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OfferBackgroundKey",
                table: "Companies");
        }
    }
}
