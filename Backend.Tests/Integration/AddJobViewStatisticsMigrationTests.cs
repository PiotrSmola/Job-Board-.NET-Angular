using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddJobViewStatistics na bazie z danymi: nowe tabele wyświetleń (klucze, kaskada po ofercie), indeksy pod
// zakresy dat, istniejące dane bez zmian, a Down usuwa tylko to, co dodała.
public class AddJobViewStatisticsMigrationTests
{
    private const string PreviousMigration = "20261106103645_AddApplicationAttachments";
    private const string AddJobViewStatistics = "20261106160206_AddJobViewStatistics";

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task<List<object?[]>> QueryAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row!);
            rows.Add(row.Select(v => v is DBNull ? null : v).ToArray());
        }

        return rows;
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql) =>
        Convert.ToInt64((await QueryAsync(connectionString, sql)).Single()[0]);

    private static Task<long> TableCountAsync(string connectionString, string table) =>
        ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'");

    private static Task<long> IndexCountAsync(string connectionString, string table, string index) =>
        ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND INDEX_NAME = '{index}'");

    private static async Task RunWithDatabaseAsync(Func<string, Task> test)
    {
        var connectionString = TestDatabase.NewConnectionString(out _);
        try
        {
            await test(connectionString);
        }
        finally
        {
            await MySqlConnection.ClearAllPoolsAsync();
            await TestDatabase.DropAsync(connectionString);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Up_CreatesTheViewTablesWithKeysAndCascadingForeignKeys()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            Assert.Equal(0, await TableCountAsync(cs, "JobOfferDailyViews"));
            Assert.Equal(0, await TableCountAsync(cs, "JobOfferViewers"));

            await MigrateToAsync(cs, AddJobViewStatistics);

            var daily = (await QueryAsync(cs,
                    "SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_KEY FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOfferDailyViews'"))
                .ToDictionary(r => (string)r[0]!, r => ((string)r[1]!, (string)r[2]!));
            Assert.Equal(("int", "PRI"), daily["JobOfferId"]);
            Assert.Equal(("date", "PRI"), daily["Date"]);
            Assert.Equal("int", daily["Views"].Item1);

            var viewers = (await QueryAsync(cs,
                    "SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_KEY FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOfferViewers'"))
                .ToDictionary(r => (string)r[0]!, r => ((string)r[1]!, (string)r[2]!));
            Assert.Equal(("binary(16)", "PRI"), viewers["VisitorHash"]);
            Assert.Equal("PRI", viewers["JobOfferId"].Item2);
            Assert.Equal("PRI", viewers["Date"].Item2);

            var foreignKeys = await QueryAsync(cs,
                "SELECT TABLE_NAME, REFERENCED_TABLE_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME IN ('JobOfferDailyViews', 'JobOfferViewers')");
            Assert.Equal(2, foreignKeys.Count);
            Assert.All(foreignKeys, fk =>
            {
                Assert.Equal("JobOffers", fk[1]);
                Assert.Equal("CASCADE", fk[2]);
            });
        });
    }

    [Fact]
    public async Task Up_AddsIndexesForDateRangesAndKeepsExistingData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await ExecuteAsync(cs, "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Stats Company', 1, UTC_TIMESTAMP(6))");
            await ExecuteAsync(cs,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) " +
                "SELECT 'Old offer', Id, 'D', 'L', 0, UTC_TIMESTAMP(6) FROM Companies LIMIT 1");

            await MigrateToAsync(cs, AddJobViewStatistics);

            Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers WHERE Title = 'Old offer'"));
            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOfferDailyViews"));
            Assert.Equal(1, await IndexCountAsync(cs, "JobOffers", "IX_JobOffers_CreatedAt"));
            Assert.Equal(1, await IndexCountAsync(cs, "JobApplications", "IX_JobApplications_AppliedAt"));
            Assert.Equal(1, await IndexCountAsync(cs, "AspNetUsers", "IX_AspNetUsers_CreatedAt"));
            Assert.Equal(1, await IndexCountAsync(cs, "JobOfferViewers", "IX_JobOfferViewers_Date"));

            // wyświetlenia znikają razem z ofertą
            await ExecuteAsync(cs, "INSERT INTO JobOfferDailyViews (JobOfferId, Date, Views) SELECT Id, '2026-01-01', 3 FROM JobOffers");
            await ExecuteAsync(cs, "DELETE FROM JobOffers");
            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOfferDailyViews"));
        });
    }

    [Fact]
    public async Task Down_DropsTheTablesAndIndexesAndKeepsTheOtherData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, AddJobViewStatistics);
            await ExecuteAsync(cs, "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Down Company', 1, UTC_TIMESTAMP(6))");

            await MigrateToAsync(cs, PreviousMigration);

            Assert.Equal(0, await TableCountAsync(cs, "JobOfferDailyViews"));
            Assert.Equal(0, await TableCountAsync(cs, "JobOfferViewers"));
            Assert.Equal(0, await IndexCountAsync(cs, "JobOffers", "IX_JobOffers_CreatedAt"));
            Assert.Equal(0, await IndexCountAsync(cs, "JobApplications", "IX_JobApplications_AppliedAt"));
            Assert.Equal(0, await IndexCountAsync(cs, "AspNetUsers", "IX_AspNetUsers_CreatedAt"));
            Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM Companies WHERE Name = 'Down Company'"));
        });
    }
}
