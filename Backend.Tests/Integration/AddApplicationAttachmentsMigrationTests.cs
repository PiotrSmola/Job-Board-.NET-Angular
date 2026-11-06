using Backend.Data;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddApplicationAttachments na bazie z danymi: nowa tabela ApplicationAttachments (klucz obcy z kaskadą,
// unikalny klucz pliku, indeks po aplikacji), istniejące aplikacje bez zmian i bez załączników, a Down usuwa tabelę
// i nie rusza reszty danych. Dane sprzed migracji wstawiane są surowym SQL-em.
public class AddApplicationAttachmentsMigrationTests
{
    private const string PreviousMigration = "20261105120246_AddCompanies";
    private const string AddApplicationAttachments = "20261106103645_AddApplicationAttachments";
    private const string Table = "ApplicationAttachments";

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

    private static async Task<bool> TableExistsAsync(string connectionString) =>
        Convert.ToInt64((await QueryAsync(connectionString,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table",
            ("@table", Table))).Single()[0]) > 0;

    private static async Task<long> CountAsync(string connectionString, string table) =>
        Convert.ToInt64((await QueryAsync(connectionString, $"SELECT COUNT(*) FROM `{table}`")).Single()[0]);

    private static Task<long> InsertAttachmentAsync(string connectionString, long applicationId, string key,
        string name = "cv.pdf", string contentType = "application/pdf", long size = 10, int kind = 0) =>
        ExecuteAsync(connectionString,
            "INSERT INTO ApplicationAttachments (JobApplicationId, Kind, FileKey, FileName, ContentType, Size, UploadedAt) " +
            "VALUES (@application, @kind, @key, @name, @type, @size, UTC_TIMESTAMP(6))",
            ("@application", applicationId), ("@kind", kind), ("@key", key), ("@name", name), ("@type", contentType), ("@size", size));

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

    private sealed record SeededApplication(long Id, int Status, string? Note, DateTime AppliedAt, DateTime StatusChangedAt);

    // aplikacje w różnych statusach na schemacie sprzed migracji (po AddCompanies)
    private static async Task<(string UserId, List<SeededApplication> Applications)> SeedApplicationsAsync(string connectionString)
    {
        var userId = Guid.NewGuid().ToString();
        await ExecuteAsync(connectionString,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
            "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, 'att@test.local', 'ATT@TEST.LOCAL', 'att@test.local', 'ATT@TEST.LOCAL', UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
            ("@id", userId));
        var companyId = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Attachment Company', 1, UTC_TIMESTAMP(6))");

        var applications = new List<SeededApplication>();
        var appliedAt = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
        var index = 0;
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            // unikalny indeks (JobOfferId, UserId) - każda aplikacja do osobnej oferty
            var offerId = await ExecuteAsync(connectionString,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) VALUES (@title, @company, 'D', 'L', 0, UTC_TIMESTAMP(6))",
                ("@title", $"T{index}"), ("@company", companyId));
            var at = appliedAt.AddDays(index);
            var note = index % 2 == 0 ? $"note {index}" : null;
            var id = await ExecuteAsync(connectionString,
                "INSERT INTO JobApplications (JobOfferId, UserId, Note, AppliedAt, Status, StatusChangedAt) VALUES (@job, @user, @note, @at, @status, @at)",
                ("@job", offerId), ("@user", userId), ("@note", note), ("@at", at), ("@status", (int)status));
            applications.Add(new SeededApplication(id, (int)status, note, at, at));
            index++;
        }

        return (userId, applications);
    }

    private static async Task<List<SeededApplication>> MigratedWithApplicationsAsync(string cs)
    {
        await MigrateToAsync(cs, PreviousMigration);
        var (_, seeded) = await SeedApplicationsAsync(cs);
        await MigrateToAsync(cs, AddApplicationAttachments);
        return seeded;
    }

    [Fact]
    public async Task Up_CreatesTheTableWithExpectedColumns()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            Assert.False(await TableExistsAsync(cs));

            await MigrateToAsync(cs, AddApplicationAttachments);

            var columns = (await QueryAsync(cs,
                    "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", ("@table", Table)))
                .ToDictionary(r => (string)r[0]!, r => (Type: (string)r[1]!, Nullable: (string)r[2]!, Default: r[3], Extra: (string)r[4]!));

            Assert.Equal(new[] { "ContentType", "FileKey", "FileName", "Id", "JobApplicationId", "Kind", "Size", "UploadedAt" },
                columns.Keys.Order().ToArray());
            Assert.Equal("int", columns["Id"].Type);
            Assert.Contains("auto_increment", columns["Id"].Extra);
            Assert.Equal("int", columns["JobApplicationId"].Type);
            Assert.Equal("int", columns["Kind"].Type);
            Assert.Equal("varchar(300)", columns["FileKey"].Type);
            Assert.Equal("varchar(255)", columns["FileName"].Type);
            Assert.Equal("varchar(100)", columns["ContentType"].Type);
            Assert.Equal("bigint", columns["Size"].Type);
            Assert.Equal("datetime(6)", columns["UploadedAt"].Type);
            Assert.All(columns.Values, c =>
            {
                Assert.Equal("NO", c.Nullable);
                Assert.Null(c.Default);
            });

            // stare kolumny CV z pierwszej wersji nie istnieją
            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobApplications' AND COLUMN_NAME LIKE 'Cv%'")).Single()[0]));
        });
    }

    [Fact]
    public async Task Up_CreatesUniqueFileKeyIndexApplicationIndexAndCascadingForeignKey()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, AddApplicationAttachments);

            var indexes = (await QueryAsync(cs,
                    "SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE FROM information_schema.STATISTICS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND INDEX_NAME <> 'PRIMARY'", ("@table", Table)))
                .ToDictionary(r => (string)r[0]!, r => (Column: (string)r[1]!, NonUnique: Convert.ToInt32(r[2])));
            Assert.Equal(("FileKey", 0), indexes["IX_ApplicationAttachments_FileKey"]);
            Assert.Equal(("JobApplicationId", 1), indexes["IX_ApplicationAttachments_JobApplicationId"]);

            var foreignKey = Assert.Single(await QueryAsync(cs,
                "SELECT REFERENCED_TABLE_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = @table", ("@table", Table)));
            Assert.Equal("JobApplications", foreignKey[0]);
            Assert.Equal("CASCADE", foreignKey[1]);
        });
    }

    [Fact]
    public async Task Up_ExistingApplicationsKeepTheirDataAndHaveNoAttachments()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (userId, seeded) = await SeedApplicationsAsync(cs);

            await MigrateToAsync(cs, AddApplicationAttachments);

            var rows = await QueryAsync(cs, "SELECT Id, UserId, Note, AppliedAt, Status, StatusChangedAt FROM JobApplications ORDER BY Id");
            Assert.Equal(seeded.Count, rows.Count);
            for (var i = 0; i < seeded.Count; i++)
            {
                Assert.Equal(seeded[i].Id, Convert.ToInt64(rows[i][0]));
                Assert.Equal(userId, rows[i][1]);
                Assert.Equal(seeded[i].Note, rows[i][2]);
                Assert.Equal(seeded[i].AppliedAt, (DateTime)rows[i][3]!);
                Assert.Equal(seeded[i].Status, Convert.ToInt32(rows[i][4]));
                Assert.Equal(seeded[i].StatusChangedAt, (DateTime)rows[i][5]!);
            }

            Assert.Equal(0, await CountAsync(cs, Table));

            // model EF też widzi aplikacje bez załączników
            await using var context = CreateContext(cs);
            var applications = await context.JobApplications.Include(a => a.Attachments).AsNoTracking().ToListAsync();
            Assert.Equal(seeded.Count, applications.Count);
            Assert.All(applications, a => Assert.Empty(a.Attachments));
        });
    }

    [Fact]
    public async Task Up_ColumnsStoreMetadataAndEnforceTheirLengthsUniquenessAndReferences()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            var seeded = await MigratedWithApplicationsAsync(cs);

            var longName = new string('ż', 255);
            var longKey = "attachments/1/" + new string('k', 286);
            var id = await InsertAttachmentAsync(cs, seeded[0].Id, longKey, longName, new string('t', 100), 5L * 1024 * 1024 * 1024, kind: 3);

            var row = Assert.Single(await QueryAsync(cs,
                "SELECT FileKey, FileName, ContentType, Size, Kind FROM ApplicationAttachments WHERE Id = @id", ("@id", id)));
            Assert.Equal(longKey, row[0]);
            Assert.Equal(longName, row[1]);
            Assert.Equal(100, ((string)row[2]!).Length);
            Assert.Equal(5L * 1024 * 1024 * 1024, Convert.ToInt64(row[3]));
            Assert.Equal(3, Convert.ToInt32(row[4]));

            // za długa wartość nie jest po cichu obcinana
            await Assert.ThrowsAsync<MySqlException>(() => InsertAttachmentAsync(cs, seeded[1].Id, new string('k', 301)));
            await Assert.ThrowsAsync<MySqlException>(() => InsertAttachmentAsync(cs, seeded[1].Id, "attachments/x/1.pdf", name: new string('n', 256)));
            await Assert.ThrowsAsync<MySqlException>(() => InsertAttachmentAsync(cs, seeded[1].Id, "attachments/x/2.pdf", contentType: new string('t', 101)));
            // ten sam klucz pliku nie może wskazywać dwóch załączników
            await Assert.ThrowsAsync<MySqlException>(() => InsertAttachmentAsync(cs, seeded[1].Id, longKey));
            // załącznik nieistniejącej aplikacji
            await Assert.ThrowsAsync<MySqlException>(() => InsertAttachmentAsync(cs, 987654321, "attachments/x/3.pdf"));
            // wymagane kolumny
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "INSERT INTO ApplicationAttachments (JobApplicationId, Kind, FileKey, FileName, ContentType, Size, UploadedAt) " +
                "VALUES (@application, 0, NULL, 'a.pdf', 'application/pdf', 1, UTC_TIMESTAMP(6))", ("@application", seeded[1].Id)));

            Assert.Equal(1, await CountAsync(cs, Table));
        });
    }

    [Fact]
    public async Task Up_DeletingAnApplicationOrItsJob_CascadesToAttachments()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            var seeded = await MigratedWithApplicationsAsync(cs);
            await InsertAttachmentAsync(cs, seeded[0].Id, "attachments/a/1.pdf");
            await InsertAttachmentAsync(cs, seeded[0].Id, "attachments/a/2.pdf");
            await InsertAttachmentAsync(cs, seeded[1].Id, "attachments/b/1.pdf");
            await InsertAttachmentAsync(cs, seeded[2].Id, "attachments/c/1.pdf");

            await ExecuteAsync(cs, "DELETE FROM JobApplications WHERE Id = @id", ("@id", seeded[0].Id));
            await ExecuteAsync(cs, "DELETE FROM JobOffers WHERE Id = (SELECT JobOfferId FROM JobApplications WHERE Id = @id)", ("@id", seeded[1].Id));

            var remaining = await QueryAsync(cs, "SELECT FileKey FROM ApplicationAttachments");
            Assert.Equal("attachments/c/1.pdf", Assert.Single(remaining)[0]);
        });
    }

    [Fact]
    public async Task Down_DropsTheTableAndKeepsApplications()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            var seeded = await MigratedWithApplicationsAsync(cs);
            await InsertAttachmentAsync(cs, seeded[0].Id, "attachments/1/x.pdf");

            await MigrateToAsync(cs, PreviousMigration);

            Assert.False(await TableExistsAsync(cs), "ApplicationAttachments should be dropped by Down");

            // aplikacje, ich statusy i notatki przetrwały cofnięcie
            var rows = await QueryAsync(cs, "SELECT Id, Note, Status FROM JobApplications ORDER BY Id");
            Assert.Equal(seeded.Select(a => (a.Id, a.Note, a.Status)).ToArray(),
                rows.Select(r => (Convert.ToInt64(r[0]), (string?)r[1], Convert.ToInt32(r[2]))).ToArray());
        });
    }

    [Fact]
    public async Task UpAgainAfterDown_RestoresAnEmptyTable()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            var seeded = await MigratedWithApplicationsAsync(cs);
            await InsertAttachmentAsync(cs, seeded[0].Id, "attachments/1/x.pdf");

            await MigrateToAsync(cs, PreviousMigration);
            await using (var context = CreateContext(cs))
            {
                await context.Database.MigrateAsync();
            }

            // dane załączników nie są odtwarzane (tabela wróciła pusta), reszta danych bez zmian
            Assert.True(await TableExistsAsync(cs));
            Assert.Equal(0, await CountAsync(cs, Table));
            Assert.Equal(seeded.Count, await CountAsync(cs, "JobApplications"));
            await InsertAttachmentAsync(cs, seeded[0].Id, "attachments/1/x.pdf");
        });
    }

    [Fact]
    public async Task Migrate_FromInitialSchemaToLatest_LeavesEveryApplicationWithoutAttachments()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            // pełna ścieżka od pierwszej migracji (stara baza użytkownika) - aplikacje sprzed zmian nie mają załączników
            await MigrateToAsync(cs, "20251221163425_InitialCreateMySQL");
            var userId = Guid.NewGuid().ToString();
            await ExecuteAsync(cs,
                "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
                "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
                "VALUES (@id, 'old@test.local', 'OLD@TEST.LOCAL', 'old@test.local', 'OLD@TEST.LOCAL', UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
                ("@id", userId));
            var jobId = await ExecuteAsync(cs,
                "INSERT INTO JobOffers (Title, Company, Description, Location, WorkType, CreatedAt) VALUES ('T', 'C', 'D', 'L', 0, UTC_TIMESTAMP(6))");
            await ExecuteAsync(cs,
                "INSERT INTO JobApplications (JobOfferId, UserId, Note, AppliedAt) VALUES (@job, @user, 'old', UTC_TIMESTAMP(6))",
                ("@job", jobId), ("@user", userId));

            await using (var context = CreateContext(cs))
            {
                await context.Database.MigrateAsync();
            }

            await using var check = CreateContext(cs);
            var application = await check.JobApplications.Include(a => a.Attachments).AsNoTracking().SingleAsync();
            Assert.Equal("old", application.Note);
            Assert.Empty(application.Attachments);
            Assert.False(check.Database.GetPendingMigrations().Any());
            Assert.Contains(AddApplicationAttachments, check.Database.GetAppliedMigrations());
        });
    }
}
