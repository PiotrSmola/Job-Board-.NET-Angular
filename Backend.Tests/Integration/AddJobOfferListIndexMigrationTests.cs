using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddJobOfferListIndex na bazie z ofertami: indeks złożony (Status, ExpiresAt, CreatedAt) pod publiczną listę
// ofert (filtr statusu i ważności, sortowanie po dacie utworzenia), dane bez zmian, Down usuwa tylko ten indeks
[Trait("Category", "Migrations")]
public class AddJobOfferListIndexMigrationTests
{
    private const string PreviousMigration = "20261106142123_AddUserAccount";
    private const string AddJobOfferListIndex = "20261106152754_AddJobOfferListIndex";
    private const string IndexName = "IX_JobOffers_Status_ExpiresAt_CreatedAt";

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options);

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        await using var context = CreateContext(connectionString);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task<List<object?[]>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

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

    // kolumny indeksów tabeli JobOffers w kolejności z indeksu
    private static async Task<Dictionary<string, string[]>> IndexesAsync(string connectionString) =>
        (await QueryAsync(connectionString,
            "SELECT INDEX_NAME, COLUMN_NAME FROM information_schema.STATISTICS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOffers' ORDER BY INDEX_NAME, SEQ_IN_INDEX"))
        .GroupBy(r => (string)r[0]!)
        .ToDictionary(g => g.Key, g => g.Select(r => (string)r[1]!).ToArray());

    private static async Task<List<string>> OffersAsync(string connectionString) =>
        (await QueryAsync(connectionString, "SELECT CONCAT_WS('|', Id, Title, Status, ExpiresAt, CreatedAt) FROM JobOffers ORDER BY Id"))
        .Select(r => (string)r[0]!)
        .ToList();

    private static async Task SeedOffersAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Index Company', 1, UTC_TIMESTAMP(6));" +
            "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt, Status, ExpiresAt) VALUES " +
            "('Active', LAST_INSERT_ID(), 'D', 'L', 0, UTC_TIMESTAMP(6), 1, NULL)," +
            "('Expiring', LAST_INSERT_ID(), 'D', 'L', 1, UTC_TIMESTAMP(6), 1, UTC_TIMESTAMP(6) + INTERVAL 5 DAY)," +
            "('Draft', LAST_INSERT_ID(), 'D', 'L', 2, UTC_TIMESTAMP(6), 0, NULL)";
        await command.ExecuteNonQueryAsync();
    }

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

    [Fact]
    public async Task Up_AddsTheCompositeIndexAndKeepsOffers()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await SeedOffersAsync(cs);
            var before = await OffersAsync(cs);
            Assert.DoesNotContain(IndexName, (await IndexesAsync(cs)).Keys);

            await MigrateToAsync(cs, AddJobOfferListIndex);

            Assert.Equal(new[] { "Status", "ExpiresAt", "CreatedAt" }, (await IndexesAsync(cs))[IndexName]);
            Assert.Equal(before, await OffersAsync(cs));
        });
    }

    [Fact]
    public async Task Down_DropsOnlyTheNewIndex_AndUpAgainRestoresIt()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await SeedOffersAsync(cs);
            var indexesBefore = await IndexesAsync(cs);
            await MigrateToAsync(cs, AddJobOfferListIndex);
            var offers = await OffersAsync(cs);

            await MigrateToAsync(cs, PreviousMigration);

            Assert.Equal(indexesBefore, await IndexesAsync(cs));
            Assert.Equal(offers, await OffersAsync(cs));

            await using (var context = CreateContext(cs))
            {
                await context.Database.MigrateAsync();
            }

            Assert.Contains(IndexName, (await IndexesAsync(cs)).Keys);
            Assert.Equal(offers, await OffersAsync(cs));
        });
    }
}
