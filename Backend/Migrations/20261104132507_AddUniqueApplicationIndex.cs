using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueApplicationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // istniejące bazy mogą mieć zduplikowane aplikacje - zostaje najstarsza (najmniejsze Id).
            // Tabela pochodna z GROUP BY jest materializowana przed usuwaniem; zwykły self-join
            // w wielotabelowym DELETE potrafi w MySQL pominąć część duplikatów.
            migrationBuilder.Sql(
                """
                DELETE a FROM `JobApplications` a
                INNER JOIN (
                    SELECT `JobOfferId`, `UserId`, MIN(`Id`) AS `KeepId`
                    FROM `JobApplications`
                    GROUP BY `JobOfferId`, `UserId`
                    HAVING COUNT(*) > 1
                ) d
                    ON a.`JobOfferId` = d.`JobOfferId`
                    AND a.`UserId` = d.`UserId`
                    AND a.`Id` > d.`KeepId`;
                """);

            // najpierw nowy indeks: MySQL wymaga indeksu dla klucza obcego JobOfferId,
            // a złożony indeks (JobOfferId, UserId) przejmuje tę rolę od starego
            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_JobOfferId_UserId",
                table: "JobApplications",
                columns: new[] { "JobOfferId", "UserId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_JobOfferId",
                table: "JobApplications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // odwrotna kolejność z tego samego powodu - klucz obcy nie może zostać bez indeksu
            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_JobOfferId",
                table: "JobApplications",
                column: "JobOfferId");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_JobOfferId_UserId",
                table: "JobApplications");
        }
    }
}
