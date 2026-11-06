using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanies : Migration
    {
        // Nazwa firmy po normalizacji (odstępy jak w CompanyNames.Normalize): białe znaki zwinięte do jednej spacji i obcięte
        // na brzegach; pusta nazwa (dane sprzed walidacji) trafia do wspólnej firmy "Unknown company"
        private const string NormalizedCompany =
            "COALESCE(NULLIF(TRIM(REGEXP_REPLACE({0}.`Company`, '[[:space:]]+', ' ')), ''), 'Unknown company')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Location = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // kolacja kolumny (domyślna bazy, ta sama co JobOffers.Company) nie rozróżnia wielkości liter -
            // indeks unikalny traktuje "TechCorp" i "techcorp" jako tę samą nazwę
            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ReviewedByUserId",
                table: "Companies",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Status",
                table: "Companies",
                column: "Status");

            // konta pracodawców - istniejący użytkownicy nie należą do żadnej firmy
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // oferty: najpierw kolumna dopuszczająca NULL uzupełniana z nazw firm, potem NOT NULL + indeks + FK
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "JobOffers",
                type: "int",
                nullable: true);

            // jedna zatwierdzona firma na każdą nazwę - grupowanie w kolacji kolumny, więc wielkość liter i odstępy
            // nie tworzą duplikatów (tak samo jak indeks unikalny); nazwa w pisowni z najstarszej oferty,
            // data utworzenia = data najstarszej oferty
            migrationBuilder.Sql(
                "INSERT INTO `Companies` (`Name`, `Status`, `CreatedAt`) " +
                "SELECT (SELECT " + Normalized("o") + " FROM `JobOffers` o " +
                "WHERE " + Normalized("o") + " = g.`Name` ORDER BY o.`CreatedAt`, o.`Id` LIMIT 1), 1, g.`CreatedAt` " +
                "FROM (SELECT " + Normalized("j") + " AS `Name`, MIN(j.`CreatedAt`) AS `CreatedAt` " +
                "FROM `JobOffers` j GROUP BY " + Normalized("j") + ") g;");

            migrationBuilder.Sql(
                "UPDATE `JobOffers` j JOIN `Companies` c ON c.`Name` = " + Normalized("j") + " SET j.`CompanyId` = c.`Id`;");

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "JobOffers",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_CompanyId",
                table: "JobOffers",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobOffers_Companies_CompanyId",
                table: "JobOffers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "Company",
                table: "JobOffers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // przywrócenie nazwy firmy w ofercie (przypisania pracodawców i firmy bez ofert są tracone)
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "JobOffers",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE `JobOffers` j JOIN `Companies` c ON c.`Id` = j.`CompanyId` SET j.`Company` = c.`Name`;");

            // kolumna jak w InitialCreateMySQL - bez wartości domyślnej
            migrationBuilder.AlterColumn<string>(
                name: "Company",
                table: "JobOffers",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // MySQL nie usunie indeksu używanego przez klucz obcy - najpierw klucz, potem indeks i kolumna
            migrationBuilder.DropForeignKey(
                name: "FK_JobOffers_Companies_CompanyId",
                table: "JobOffers");

            migrationBuilder.DropIndex(
                name: "IX_JobOffers_CompanyId",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "JobOffers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Companies");
        }

        private static string Normalized(string alias) => string.Format(NormalizedCompany, alias);
    }
}
