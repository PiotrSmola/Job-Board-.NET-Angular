using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddCompanies na bazie z danymi: oferty miały tekstową nazwę firmy (kolumna JobOffers.Company),
// po migracji wskazują encję Company; Down przywraca kolumnę tekstową.
// Dane sprzed migracji wstawiane są surowym SQL-em (bieżący model EF zna już nowy schemat).
[Trait("Category", "Migrations")]
public class AddCompaniesMigrationTests
{
    private const string PreviousMigration = "20261104135648_AddRecruitmentWorkflow";
    private const string AddCompanies = "20261105120246_AddCompanies";

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

    private static async Task<long> ScalarAsync(string connectionString, string sql) =>
        Convert.ToInt64((await QueryAsync(connectionString, sql)).Single()[0]);

    private static Task<long> InsertOfferAsync(string connectionString, string? company, DateTime createdAt, string title = "Offer") =>
        ExecuteAsync(connectionString,
            "INSERT INTO JobOffers (Title, Company, Description, Location, WorkType, CreatedAt) " +
            "VALUES (@title, @company, 'D', 'L', 0, @createdAt)",
            ("@title", title), ("@company", company), ("@createdAt", createdAt));

    private static async Task<string> InsertUserAsync(string connectionString, string email)
    {
        var id = Guid.NewGuid().ToString();
        await ExecuteAsync(connectionString,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
            "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, @email, UPPER(@email), @email, UPPER(@email), UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
            ("@id", id), ("@email", email));
        return id;
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string table, string column) =>
        (await ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'")) > 0;

    private static async Task<bool> TableExistsAsync(string connectionString, string table) =>
        (await ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'")) > 0;

    private static async Task<bool> ForeignKeyExistsAsync(string connectionString, string name) =>
        (await ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = '{name}' AND CONSTRAINT_TYPE = 'FOREIGN KEY'")) > 0;

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

    // oferty o zróżnicowanych nazwach firm: duplikaty co do wielkości liter, odstępów, tabulatorów i puste nazwy
    private sealed record SeededOffers(long TechOldest, long TechLower, long TechUpper, long BetaTab, long BetaSpaces, long Empty, long Blank, long Unique, DateTime TechCreatedAt);

    private static async Task<SeededOffers> SeedMessyOffersAsync(string connectionString)
    {
        var d1 = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        var d2 = d1.AddDays(3);
        var d3 = d1.AddDays(9);

        // najnowsza oferta wstawiona jako pierwsza - kolejność zależy od CreatedAt, nie od Id
        var techUpper = await InsertOfferAsync(connectionString, "TECHCORP SOLUTIONS", d3, "Tech upper");
        var techLower = await InsertOfferAsync(connectionString, "techcorp solutions", d2, "Tech lower");
        var techOldest = await InsertOfferAsync(connectionString, "  TechCorp   Solutions ", d1, "Tech oldest");
        var betaTab = await InsertOfferAsync(connectionString, "Beta\tInc", d2, "Beta tab");
        var betaSpaces = await InsertOfferAsync(connectionString, "beta   inc", d3, "Beta spaces");
        var empty = await InsertOfferAsync(connectionString, "", d1, "Empty name");
        var blank = await InsertOfferAsync(connectionString, "   ", d2, "Blank name");
        var unique = await InsertOfferAsync(connectionString, "Unique Ltd", d2, "Unique");
        return new SeededOffers(techOldest, techLower, techUpper, betaTab, betaSpaces, empty, blank, unique, d1);
    }

    private static async Task<Dictionary<long, (long CompanyId, string Name)>> OfferCompaniesAsync(string connectionString)
    {
        var rows = await QueryAsync(connectionString,
            "SELECT j.Id, c.Id, c.Name FROM JobOffers j JOIN Companies c ON c.Id = j.CompanyId");
        return rows.ToDictionary(r => Convert.ToInt64(r[0]), r => (Convert.ToInt64(r[1]), (string)r[2]!));
    }

    [Fact]
    public async Task Up_BackfillsOneApprovedCompanyPerNormalizedName()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var offers = await SeedMessyOffersAsync(cs);

            await MigrateToAsync(cs, AddCompanies);

            var companies = await QueryAsync(cs, "SELECT Name, Status, ReviewedAt, ReviewedByUserId, Description, Website, Location, RejectionReason FROM Companies ORDER BY Name");
            Assert.Equal(
                new[] { "Beta Inc", "TechCorp Solutions", "Unique Ltd", "Unknown company" },
                companies.Select(c => (string)c[0]!).ToArray());
            // wszystkie firmy z migracji są zatwierdzone, bez danych opcjonalnych i bez decyzji admina
            Assert.All(companies, c =>
            {
                Assert.Equal(1, Convert.ToInt32(c[1]));
                Assert.All(c.Skip(2), Assert.Null);
            });

            var assigned = await OfferCompaniesAsync(cs);
            Assert.Equal(8, assigned.Count);
            Assert.Single(new[] { offers.TechOldest, offers.TechLower, offers.TechUpper }.Select(id => assigned[id].CompanyId).Distinct());
            Assert.Single(new[] { offers.BetaTab, offers.BetaSpaces }.Select(id => assigned[id].CompanyId).Distinct());
            Assert.Single(new[] { offers.Empty, offers.Blank }.Select(id => assigned[id].CompanyId).Distinct());
            Assert.Equal(4, assigned.Values.Select(v => v.CompanyId).Distinct().Count());
            Assert.Equal("Unique Ltd", assigned[offers.Unique].Name);
            Assert.Equal("Unknown company", assigned[offers.Empty].Name);
        });
    }

    [Fact]
    public async Task Up_UsesSpellingAndCreationDateOfTheOldestOffer()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var offers = await SeedMessyOffersAsync(cs);

            await MigrateToAsync(cs, AddCompanies);

            var rows = await QueryAsync(cs, "SELECT Name, CreatedAt FROM Companies WHERE Name = 'TechCorp Solutions'");
            var row = Assert.Single(rows);
            Assert.Equal("TechCorp Solutions", row[0]);
            Assert.Equal(offers.TechCreatedAt, (DateTime)row[1]!);
        });
    }

    [Fact]
    public async Task Up_EveryOfferGetsCompanyIdAndKeepsItsData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await SeedMessyOffersAsync(cs);
            var titlesBefore = (await QueryAsync(cs, "SELECT Title FROM JobOffers ORDER BY Id")).Select(r => (string)r[0]!).ToArray();

            await MigrateToAsync(cs, AddCompanies);

            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers WHERE CompanyId IS NULL"));
            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers j LEFT JOIN Companies c ON c.Id = j.CompanyId WHERE c.Id IS NULL"));
            var titlesAfter = (await QueryAsync(cs, "SELECT Title FROM JobOffers ORDER BY Id")).Select(r => (string)r[0]!).ToArray();
            Assert.Equal(titlesBefore, titlesAfter);
            Assert.Equal(1, await ScalarAsync(cs, "SELECT MIN(Status) FROM JobOffers"));
        });
    }

    [Fact]
    public async Task Up_DropsTextColumn_AndCreatesUniqueNameIndexAndForeignKeys()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await InsertOfferAsync(cs, "Acme", DateTime.UtcNow);
            Assert.True(await ColumnExistsAsync(cs, "JobOffers", "Company"));

            await MigrateToAsync(cs, AddCompanies);

            Assert.False(await ColumnExistsAsync(cs, "JobOffers", "Company"));
            Assert.True(await ColumnExistsAsync(cs, "JobOffers", "CompanyId"));
            Assert.True(await ColumnExistsAsync(cs, "AspNetUsers", "CompanyId"));
            Assert.True(await TableExistsAsync(cs, "Companies"));
            Assert.True(await ForeignKeyExistsAsync(cs, "FK_JobOffers_Companies_CompanyId"));
            Assert.True(await ForeignKeyExistsAsync(cs, "FK_AspNetUsers_Companies_CompanyId"));
            Assert.True(await ForeignKeyExistsAsync(cs, "FK_Companies_AspNetUsers_ReviewedByUserId"));
            Assert.Equal("NO", (string)(await QueryAsync(cs,
                "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOffers' AND COLUMN_NAME = 'CompanyId'")).Single()[0]!);
            Assert.Equal(0, await ScalarAsync(cs,
                "SELECT NON_UNIQUE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Companies' AND INDEX_NAME = 'IX_Companies_Name'"));
        });
    }

    [Fact]
    public async Task Up_EnforcesCaseInsensitiveUniqueNameAndRestrictedDeletes()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await InsertOfferAsync(cs, "Acme", DateTime.UtcNow);
            await MigrateToAsync(cs, AddCompanies);

            // ta sama nazwa w innej pisowni narusza indeks unikalny
            var duplicate = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('ACME', 0, UTC_TIMESTAMP(6))"));
            Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, duplicate.ErrorCode);

            // firma z ofertami nie może zostać usunięta (klucz obcy Restrict)
            var restricted = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs, "DELETE FROM Companies WHERE Name = 'Acme'"));
            Assert.Equal(MySqlErrorCode.RowIsReferenced2, restricted.ErrorCode);

            // oferta bez firmy jest odrzucana
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "INSERT INTO JobOffers (Title, Description, Location, WorkType, CreatedAt, CompanyId) VALUES ('X', 'D', 'L', 0, UTC_TIMESTAMP(6), 999999)"));
        });
    }

    [Fact]
    public async Task Up_KeepsExistingUsersWithoutCompanyAndApplicationsAttachedToOffers()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var userId = await InsertUserAsync(cs, "candidate@test.local");
            var offerId = await InsertOfferAsync(cs, "Acme", DateTime.UtcNow);
            await ExecuteAsync(cs,
                "INSERT INTO JobApplications (JobOfferId, UserId, AppliedAt) VALUES (@job, @user, UTC_TIMESTAMP(6))",
                ("@job", offerId), ("@user", userId));

            await MigrateToAsync(cs, AddCompanies);

            Assert.Null((await QueryAsync(cs, "SELECT CompanyId FROM AspNetUsers WHERE Id = @id", ("@id", userId))).Single()[0]);
            var application = (await QueryAsync(cs,
                "SELECT a.JobOfferId, c.Name FROM JobApplications a JOIN JobOffers j ON j.Id = a.JobOfferId JOIN Companies c ON c.Id = j.CompanyId")).Single();
            Assert.Equal(offerId, Convert.ToInt64(application[0]));
            Assert.Equal("Acme", application[1]);
        });
    }

    [Fact]
    public async Task Up_OnDatabaseWithoutOffers_CreatesNoCompanies()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);

            await MigrateToAsync(cs, AddCompanies);

            Assert.True(await TableExistsAsync(cs, "Companies"));
            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM Companies"));
        });
    }

    [Fact]
    public async Task Up_PreservesVeryLongCompanyNameAfterTrimming()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var name = new string('A', 186) + " " + new string('b', 9);
            await InsertOfferAsync(cs, "  " + name + "  ", DateTime.UtcNow); // razem 200 znaków (limit kolumny)

            await MigrateToAsync(cs, AddCompanies);

            Assert.Equal(name, (string)(await QueryAsync(cs, "SELECT Name FROM Companies")).Single()[0]!);
        });
    }

    [Fact]
    public async Task Up_WorksWithModelOfCurrentContext()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var offers = await SeedMessyOffersAsync(cs);

            await using var context = CreateContext(cs);
            await context.Database.MigrateAsync();

            var job = await context.JobOffers.AsNoTracking().Include(j => j.Company).SingleAsync(j => j.Id == offers.TechUpper);
            Assert.Equal("TechCorp Solutions", job.Company.Name);
            Assert.Equal(Backend.Models.CompanyStatus.Approved, job.Company.Status);
            Assert.Equal(3, await context.JobOffers.CountAsync(j => j.CompanyId == job.CompanyId));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        });
    }

    // --- Down ---

    [Fact]
    public async Task Down_RestoresTextColumnWithCompanyNames()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var offers = await SeedMessyOffersAsync(cs);
            await MigrateToAsync(cs, AddCompanies);

            await MigrateToAsync(cs, PreviousMigration);

            Assert.True(await ColumnExistsAsync(cs, "JobOffers", "Company"));
            Assert.False(await ColumnExistsAsync(cs, "JobOffers", "CompanyId"));
            Assert.False(await ColumnExistsAsync(cs, "AspNetUsers", "CompanyId"));
            Assert.False(await TableExistsAsync(cs, "Companies"));
            Assert.False(await ForeignKeyExistsAsync(cs, "FK_JobOffers_Companies_CompanyId"));
            Assert.Equal("NO", (string)(await QueryAsync(cs,
                "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOffers' AND COLUMN_NAME = 'Company'")).Single()[0]!);

            var names = (await QueryAsync(cs, "SELECT Id, Company FROM JobOffers"))
                .ToDictionary(r => Convert.ToInt64(r[0]), r => (string)r[1]!);
            Assert.Equal(8, names.Count);
            Assert.All(new[] { offers.TechOldest, offers.TechLower, offers.TechUpper }, id => Assert.Equal("TechCorp Solutions", names[id]));
            Assert.All(new[] { offers.BetaTab, offers.BetaSpaces }, id => Assert.Equal("Beta Inc", names[id]));
            Assert.All(new[] { offers.Empty, offers.Blank }, id => Assert.Equal("Unknown company", names[id]));
            Assert.Equal("Unique Ltd", names[offers.Unique]);
        });
    }

    [Fact]
    public async Task Down_KeepsEmployerAccountsButDropsTheirCompanyAssignment()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await InsertOfferAsync(cs, "Acme", DateTime.UtcNow);
            await MigrateToAsync(cs, AddCompanies);
            var employerId = await InsertUserAsync(cs, "employer@test.local");
            await ExecuteAsync(cs, "UPDATE AspNetUsers SET CompanyId = (SELECT MIN(Id) FROM Companies) WHERE Id = @id", ("@id", employerId));
            await ExecuteAsync(cs, "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Without offers', 0, UTC_TIMESTAMP(6))");

            await MigrateToAsync(cs, PreviousMigration);

            Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM AspNetUsers WHERE Email = 'employer@test.local'"));
            Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers"));
            Assert.Equal("Acme", (string)(await QueryAsync(cs, "SELECT Company FROM JobOffers")).Single()[0]!);
        });
    }

    [Fact]
    public async Task Down_ThenUpAgain_BackfillsCompaniesAgain()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await SeedMessyOffersAsync(cs);
            await MigrateToAsync(cs, AddCompanies);
            var offersBefore = await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers");

            await MigrateToAsync(cs, PreviousMigration);
            await MigrateToAsync(cs, AddCompanies);

            Assert.Equal(offersBefore, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers"));
            Assert.Equal(4, await ScalarAsync(cs, "SELECT COUNT(*) FROM Companies"));
            Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM JobOffers WHERE CompanyId IS NULL"));
            var names = (await QueryAsync(cs, "SELECT Name FROM Companies ORDER BY Name")).Select(r => (string)r[0]!).ToArray();
            Assert.Equal(new[] { "Beta Inc", "TechCorp Solutions", "Unique Ltd", "Unknown company" }, names);
        });
    }

    [Fact]
    public async Task Down_OnDatabaseWithNewOffersCreatedAfterUp_WritesTheirCompanyNames()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            await MigrateToAsync(cs, AddCompanies);
            var companyId = await ExecuteAsync(cs, "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Fresh Company', 1, UTC_TIMESTAMP(6))");
            await ExecuteAsync(cs,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) VALUES ('New', @company, 'D', 'L', 0, UTC_TIMESTAMP(6))",
                ("@company", companyId));

            await MigrateToAsync(cs, PreviousMigration);

            Assert.Equal("Fresh Company", (string)(await QueryAsync(cs, "SELECT Company FROM JobOffers WHERE Title = 'New'")).Single()[0]!);
        });
    }
}
