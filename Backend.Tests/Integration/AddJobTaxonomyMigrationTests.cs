using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddJobTaxonomy na bazie z danymi: istniejące oferty dostają wartości domyślne (PLN, miesięcznie, bez typów
// umów, bez kategorii i poziomu) i zachowują dane, nowe tabele mają unikalne klucze i kaskady, a Down wraca do
// poprzedniego schematu bez utraty ofert. Dane sprzed migracji wstawiane są surowym SQL-em.
[Trait("Category", "Migrations")]
public class AddJobTaxonomyMigrationTests
{
    private const string PreviousMigration = "20261106152754_AddJobOfferListIndex";
    private const string AddJobTaxonomy = "20261106160558_AddJobTaxonomy";

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

    // oferty na schemacie sprzed migracji (po AddApplicationAttachments), w tym jedna z aplikacją
    private static async Task<long[]> SeedOffersAsync(string connectionString)
    {
        var companyId = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Taxonomy Company', 1, UTC_TIMESTAMP(6))");
        var ids = new List<long>();
        foreach (var (title, status) in new[] { ("Old offer A", 1), ("Old offer B", 0), ("Old offer C", 2) })
        {
            ids.Add(await ExecuteAsync(connectionString,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, SalaryMin, SalaryMax, CreatedAt, Status) " +
                "VALUES (@title, @company, 'Desc', 'Rzeszow', 1, 5000, 9000, UTC_TIMESTAMP(6), @status)",
                ("@title", title), ("@company", companyId), ("@status", status)));
        }

        var userId = Guid.NewGuid().ToString();
        await ExecuteAsync(connectionString,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
            "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, 'tax@test.local', 'TAX@TEST.LOCAL', 'tax@test.local', 'TAX@TEST.LOCAL', UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
            ("@id", userId));
        await ExecuteAsync(connectionString,
            "INSERT INTO JobApplications (JobOfferId, UserId, AppliedAt, Status, StatusChangedAt) " +
            "VALUES (@job, @user, UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6))", ("@job", ids[0]), ("@user", userId));

        return ids.ToArray();
    }

    [Fact]
    public async Task Up_ExistingOffersGetDefaultsAndKeepTheirData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var ids = await SeedOffersAsync(cs);

            await MigrateToAsync(cs, AddJobTaxonomy);

            var rows = await QueryAsync(cs,
                "SELECT Id, Title, SalaryMin, SalaryMax, WorkType, Status, CategoryId, Seniority, ContractTypes, SalaryCurrency, SalaryPeriod " +
                "FROM JobOffers ORDER BY Id");
            Assert.Equal(ids, rows.Select(r => Convert.ToInt64(r[0])).ToArray());
            Assert.Equal(new[] { "Old offer A", "Old offer B", "Old offer C" }, rows.Select(r => (string)r[1]!).ToArray());
            Assert.All(rows, r =>
            {
                Assert.Equal(5000m, Convert.ToDecimal(r[2]));
                Assert.Equal(9000m, Convert.ToDecimal(r[3]));
                Assert.Equal(1, Convert.ToInt32(r[4]));
                Assert.Null(r[6]);
                Assert.Null(r[7]);
                // wartości domyślne: bez typów umów, PLN, miesięcznie
                Assert.Equal(0, Convert.ToInt32(r[8]));
                Assert.Equal(0, Convert.ToInt32(r[9]));
                Assert.Equal(0, Convert.ToInt32(r[10]));
            });
            Assert.Equal(new[] { 1, 0, 2 }, rows.Select(r => Convert.ToInt32(r[5])).ToArray());
            Assert.Equal(1, await CountAsync(cs, "JobApplications"));

            // model EF czyta oferty bez kategorii i umiejętności
            await using var context = CreateContext(cs);
            var offers = await context.JobOffers.Include(o => o.Skills).Include(o => o.Category).AsNoTracking().ToListAsync();
            Assert.Equal(3, offers.Count);
            Assert.All(offers, o =>
            {
                Assert.Null(o.Category);
                Assert.Empty(o.Skills);
            });
        });
    }

    [Fact]
    public async Task Up_CreatesTablesWithUniqueKeysAndCascades()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            Assert.False(await TableExistsAsync(cs, "Categories"));
            Assert.False(await TableExistsAsync(cs, "Skills"));

            await MigrateToAsync(cs, AddJobTaxonomy);

            var uniqueIndexes = (await QueryAsync(cs,
                    "SELECT TABLE_NAME, INDEX_NAME FROM information_schema.STATISTICS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 AND TABLE_NAME IN ('Categories', 'Skills')"))
                .Select(r => $"{r[0]}.{r[1]}").ToHashSet();
            Assert.Contains("Categories.IX_Categories_Name", uniqueIndexes);
            Assert.Contains("Categories.IX_Categories_Slug", uniqueIndexes);
            Assert.Contains("Skills.IX_Skills_NormalizedName", uniqueIndexes);

            var indexedJobColumns = (await QueryAsync(cs,
                    "SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOffers'"))
                .Select(r => (string)r[0]!).ToHashSet();
            Assert.Superset(new HashSet<string> { "CategoryId", "Seniority", "SalaryCurrency" }, indexedJobColumns);

            var rules = (await QueryAsync(cs,
                    "SELECT TABLE_NAME, REFERENCED_TABLE_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                    "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME IN ('JobOffers', 'JobOfferSkills') " +
                    "AND REFERENCED_TABLE_NAME IN ('Categories', 'Skills', 'JobOffers')"))
                .Select(r => $"{r[0]}->{r[1]}:{r[2]}").ToHashSet();
            Assert.Contains("JobOffers->Categories:SET NULL", rules);
            Assert.Contains("JobOfferSkills->JobOffers:CASCADE", rules);
            Assert.Contains("JobOfferSkills->Skills:CASCADE", rules);

            await ExecuteAsync(cs, "INSERT INTO Categories (Name, Slug, SortOrder) VALUES ('Cat', 'cat', 1)");
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs, "INSERT INTO Categories (Name, Slug, SortOrder) VALUES ('CAT', 'other', 2)"));
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs, "INSERT INTO Categories (Name, Slug, SortOrder) VALUES ('Other', 'cat', 2)"));

            // klucz umiejętności jest dokładny (kolacja binarna): różni się wielkością liter, ale nie powtarza
            await ExecuteAsync(cs, "INSERT INTO Skills (Name, NormalizedName) VALUES ('C#', 'c#')");
            await ExecuteAsync(cs, "INSERT INTO Skills (Name, NormalizedName) VALUES ('Upper', 'UPPER')");
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs, "INSERT INTO Skills (Name, NormalizedName) VALUES ('C#2', 'c#')"));
        });
    }

    [Fact]
    public async Task Up_DeletingACategoryKeepsOffers_AndDeletingAnOfferRemovesItsSkillLinks()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var ids = await SeedOffersAsync(cs);
            await MigrateToAsync(cs, AddJobTaxonomy);

            var categoryId = await ExecuteAsync(cs, "INSERT INTO Categories (Name, Slug, SortOrder) VALUES ('Cat', 'cat', 1)");
            await ExecuteAsync(cs, "UPDATE JobOffers SET CategoryId = @category WHERE Id = @id", ("@category", categoryId), ("@id", ids[1]));
            var skillId = await ExecuteAsync(cs, "INSERT INTO Skills (Name, NormalizedName) VALUES ('Go', 'go')");
            await ExecuteAsync(cs, "INSERT INTO JobOfferSkills (JobOffersId, SkillsId) VALUES (@job, @skill)", ("@job", ids[1]), ("@skill", skillId));

            await ExecuteAsync(cs, "DELETE FROM Categories WHERE Id = @id", ("@id", categoryId));
            Assert.Null(Assert.Single(await QueryAsync(cs, "SELECT CategoryId FROM JobOffers WHERE Id = @id", ("@id", ids[1])))[0]);
            Assert.Equal(3, await CountAsync(cs, "JobOffers"));

            await ExecuteAsync(cs, "DELETE FROM JobOffers WHERE Id = @id", ("@id", ids[1]));
            Assert.Equal(0, await CountAsync(cs, "JobOfferSkills"));
            Assert.Equal(1, await CountAsync(cs, "Skills"));
        });
    }

    [Fact]
    public async Task Down_RestoresThePreviousSchemaAndKeepsOffers()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await SeedOffersAsync(cs);
            await MigrateToAsync(cs, AddJobTaxonomy);
            await ExecuteAsync(cs, "UPDATE JobOffers SET Seniority = 3, ContractTypes = 6, SalaryCurrency = 2");

            await MigrateToAsync(cs, PreviousMigration);

            foreach (var table in new[] { "Categories", "Skills", "JobOfferSkills" })
            {
                Assert.False(await TableExistsAsync(cs, table), table);
            }

            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOffers' " +
                "AND COLUMN_NAME IN ('CategoryId', 'Seniority', 'ContractTypes', 'SalaryCurrency', 'SalaryPeriod')")).Single()[0]));
            Assert.Equal(3, await CountAsync(cs, "JobOffers"));

            // i znów w górę
            await MigrateToAsync(cs, AddJobTaxonomy);
            Assert.Equal(3, await CountAsync(cs, "JobOffers"));
            Assert.All(await QueryAsync(cs, "SELECT Seniority, ContractTypes, SalaryCurrency FROM JobOffers"), r =>
            {
                Assert.Null(r[0]);
                Assert.Equal(0, Convert.ToInt32(r[1]));
                Assert.Equal(0, Convert.ToInt32(r[2]));
            });
        });
    }
}
