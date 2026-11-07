using Backend.Data;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddCompanyMedia na bazie z danymi: istniejące firmy dostają domyślne ustawienia tła (bez obrazu,
// przyciemnienie 75, Center), tabela zdjęć ma unikalny klucz pliku i kaskadę od firmy, a Down wraca do poprzedniego
// schematu bez utraty firm. Dane sprzed migracji wstawiane są surowym SQL-em.
[Trait("Category", "Migrations")]
public class AddCompanyMediaMigrationTests
{
    private const string PreviousMigration = "20261106160558_AddJobTaxonomy";
    private const string AddCompanyMedia = "20261107150724_AddCompanyMedia";

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options);

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        await using var context = CreateContext(connectionString);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task<long> ExecuteAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
        return command.LastInsertedId;
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

    private static async Task<bool> TableExistsAsync(string connectionString, string table) =>
        Convert.ToInt64((await QueryAsync(connectionString,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table",
            ("@table", table))).Single()[0]) > 0;

    private static async Task<long> CountAsync(string connectionString, string table) =>
        Convert.ToInt64((await QueryAsync(connectionString, $"SELECT COUNT(*) FROM `{table}`")).Single()[0]);

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

    // firmy na schemacie sprzed migracji (zatwierdzona z ofertą i oczekująca)
    private static async Task<long[]> SeedCompaniesAsync(string connectionString)
    {
        var approved = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Description, Status, CreatedAt) VALUES ('Media Company', 'About us', 1, UTC_TIMESTAMP(6))");
        var pending = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Pending Media Company', 0, UTC_TIMESTAMP(6))");
        await ExecuteAsync(connectionString,
            "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt, Status) " +
            "VALUES ('Offer', @company, 'Desc', 'Rzeszow', 1, UTC_TIMESTAMP(6), 1)", ("@company", approved));
        return [approved, pending];
    }

    private static Task<long> InsertPhotoAsync(string connectionString, long companyId, string fileKey) =>
        ExecuteAsync(connectionString,
            "INSERT INTO CompanyPhotos (CompanyId, FileKey, ContentType, Size, Width, Height, SortOrder, UploadedAt) " +
            "VALUES (@company, @key, 'image/png', 100, 640, 480, 0, UTC_TIMESTAMP(6))",
            ("@company", companyId), ("@key", fileKey));

    [Fact]
    public async Task Up_ExistingCompaniesGetDefaultBackgroundSettingsAndKeepTheirData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var ids = await SeedCompaniesAsync(cs);

            await MigrateToAsync(cs, AddCompanyMedia);

            var rows = await QueryAsync(cs,
                "SELECT Id, Name, Status, OfferBackgroundKey, OfferBackgroundContentType, OfferBackgroundDim, OfferBackgroundFocus " +
                "FROM Companies ORDER BY Id");
            Assert.Equal(ids, rows.Select(r => Convert.ToInt64(r[0])).ToArray());
            Assert.Equal(new[] { "Media Company", "Pending Media Company" }, rows.Select(r => (string)r[1]!).ToArray());
            Assert.Equal(new[] { 1, 0 }, rows.Select(r => Convert.ToInt32(r[2])).ToArray());
            Assert.All(rows, r =>
            {
                Assert.Null(r[3]);
                Assert.Null(r[4]);
                Assert.Equal(75, Convert.ToInt32(r[5]));
                Assert.Equal(0, Convert.ToInt32(r[6]));
            });
            Assert.Equal(1, await CountAsync(cs, "JobOffers"));

            // model EF czyta firmy z nowymi kolumnami i (pustą) galerią
            await using var context = CreateContext(cs);
            var companies = await context.Companies.Include(c => c.Photos).AsNoTracking().OrderBy(c => c.Id).ToListAsync();
            Assert.Equal(2, companies.Count);
            Assert.All(companies, c =>
            {
                Assert.Null(c.OfferBackgroundKey);
                Assert.Equal(75, c.OfferBackgroundDim);
                Assert.Equal(OfferBackgroundFocus.Center, c.OfferBackgroundFocus);
                Assert.Empty(c.Photos);
            });
        });
    }

    [Fact]
    public async Task Up_PhotosHaveUniqueFileKeysAndAreDeletedWithTheirCompany()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var ids = await SeedCompaniesAsync(cs);
            Assert.False(await TableExistsAsync(cs, "CompanyPhotos"));

            await MigrateToAsync(cs, AddCompanyMedia);

            var uniqueIndexes = (await QueryAsync(cs,
                    "SELECT INDEX_NAME FROM information_schema.STATISTICS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 AND TABLE_NAME = 'CompanyPhotos'"))
                .Select(r => (string)r[0]!).ToHashSet();
            Assert.Contains("IX_CompanyPhotos_FileKey", uniqueIndexes);

            var rules = (await QueryAsync(cs,
                    "SELECT REFERENCED_TABLE_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                    "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'CompanyPhotos'"))
                .Select(r => $"{r[0]}:{r[1]}").ToList();
            Assert.Equal(["Companies:CASCADE"], rules);

            await InsertPhotoAsync(cs, ids[1], "companies/2/photos/a.png");
            await InsertPhotoAsync(cs, ids[1], "companies/2/photos/b.png");
            await InsertPhotoAsync(cs, ids[0], "companies/1/photos/c.png");
            await Assert.ThrowsAsync<MySqlException>(() => InsertPhotoAsync(cs, ids[0], "companies/2/photos/a.png"));
            await Assert.ThrowsAsync<MySqlException>(() => InsertPhotoAsync(cs, 999999, "companies/x/photos/d.png"));

            await ExecuteAsync(cs, "DELETE FROM Companies WHERE Id = @id", ("@id", ids[1]));
            Assert.Equal(1, await CountAsync(cs, "CompanyPhotos"));
        });
    }

    [Fact]
    public async Task Down_RestoresThePreviousSchemaAndKeepsCompanies()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var ids = await SeedCompaniesAsync(cs);
            await MigrateToAsync(cs, AddCompanyMedia);
            await InsertPhotoAsync(cs, ids[0], "companies/1/photos/a.png");
            await ExecuteAsync(cs, "UPDATE Companies SET OfferBackgroundKey = 'companies/1/offer-background/b.png', " +
                                   "OfferBackgroundContentType = 'image/png', OfferBackgroundDim = 60, OfferBackgroundFocus = 2");

            await MigrateToAsync(cs, PreviousMigration);

            Assert.False(await TableExistsAsync(cs, "CompanyPhotos"));
            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Companies' " +
                "AND COLUMN_NAME LIKE 'OfferBackground%'")).Single()[0]));
            Assert.Equal(2, await CountAsync(cs, "Companies"));
            Assert.Equal(1, await CountAsync(cs, "JobOffers"));

            // i znów w górę - wartości domyślne od nowa
            await MigrateToAsync(cs, AddCompanyMedia);
            Assert.All(await QueryAsync(cs, "SELECT OfferBackgroundKey, OfferBackgroundDim, OfferBackgroundFocus FROM Companies"), r =>
            {
                Assert.Null(r[0]);
                Assert.Equal(75, Convert.ToInt32(r[1]));
                Assert.Equal(0, Convert.ToInt32(r[2]));
            });
        });
    }
}
