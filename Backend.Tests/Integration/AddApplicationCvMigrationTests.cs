using Backend.Data;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddApplicationCv na bazie z danymi: pięć nowych kolumn CV w JobApplications jest nullable,
// istniejące aplikacje zostają bez CV (bez backfillu), a Down usuwa kolumny i nie rusza reszty danych.
// Dane sprzed migracji wstawiane są surowym SQL-em (bieżący model EF zna już nowe kolumny).
public class AddApplicationCvMigrationTests
{
    private const string PreviousMigration = "20261105120246_AddCompanies";
    private const string AddApplicationCv = "20261106090927_AddApplicationCv";

    private static readonly string[] CvColumns = ["CvFileKey", "CvFileName", "CvContentType", "CvSize", "CvUploadedAt"];

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

    private static async Task<bool> ColumnExistsAsync(string connectionString, string column) =>
        Convert.ToInt64((await QueryAsync(connectionString,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobApplications' AND COLUMN_NAME = @column",
            ("@column", column))).Single()[0]) > 0;

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
    private static async Task<(string UserId, long JobId, List<SeededApplication> Applications)> SeedApplicationsAsync(string connectionString)
    {
        var userId = Guid.NewGuid().ToString();
        await ExecuteAsync(connectionString,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
            "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, 'cv@test.local', 'CV@TEST.LOCAL', 'cv@test.local', 'CV@TEST.LOCAL', UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
            ("@id", userId));
        var companyId = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Cv Company', 1, UTC_TIMESTAMP(6))");

        var jobId = await ExecuteAsync(connectionString,
            "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) VALUES ('T', @company, 'D', 'L', 0, UTC_TIMESTAMP(6))",
            ("@company", companyId));

        var applications = new List<SeededApplication>();
        var appliedAt = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
        var index = 0;
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            // unikalny indeks (JobOfferId, UserId) - każda aplikacja do osobnej oferty
            var offerId = index == 0 ? jobId : await ExecuteAsync(connectionString,
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

        return (userId, jobId, applications);
    }

    [Fact]
    public async Task Up_AddsFiveNullableColumnsWithExpectedTypes()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            foreach (var column in CvColumns)
            {
                Assert.False(await ColumnExistsAsync(cs, column));
            }

            await MigrateToAsync(cs, AddApplicationCv);

            var columns = (await QueryAsync(cs,
                    "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobApplications' AND COLUMN_NAME LIKE 'Cv%'"))
                .ToDictionary(r => (string)r[0]!, r => (Type: (string)r[1]!, Nullable: (string)r[2]!, Default: r[3]));

            Assert.Equal(CvColumns.Order().ToArray(), columns.Keys.Order().ToArray());
            Assert.Equal("varchar(300)", columns["CvFileKey"].Type);
            Assert.Equal("varchar(255)", columns["CvFileName"].Type);
            Assert.Equal("varchar(100)", columns["CvContentType"].Type);
            Assert.Equal("bigint", columns["CvSize"].Type);
            Assert.Equal("datetime(6)", columns["CvUploadedAt"].Type);
            Assert.All(columns.Values, c =>
            {
                Assert.Equal("YES", c.Nullable);
                Assert.Null(c.Default);
            });
        });
    }

    [Fact]
    public async Task Up_ExistingApplicationsHaveNoCvAndKeepTheirData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (userId, _, seeded) = await SeedApplicationsAsync(cs);

            await MigrateToAsync(cs, AddApplicationCv);

            // wszystkie kolumny CV są NULL (bez backfillu), a dotychczasowe dane bez zmian
            var rows = await QueryAsync(cs,
                "SELECT Id, UserId, Note, AppliedAt, Status, StatusChangedAt, CvFileKey, CvFileName, CvContentType, CvSize, CvUploadedAt " +
                "FROM JobApplications ORDER BY Id");
            Assert.Equal(seeded.Count, rows.Count);
            for (var i = 0; i < seeded.Count; i++)
            {
                Assert.Equal(seeded[i].Id, Convert.ToInt64(rows[i][0]));
                Assert.Equal(userId, rows[i][1]);
                Assert.Equal(seeded[i].Note, rows[i][2]);
                Assert.Equal(seeded[i].AppliedAt, (DateTime)rows[i][3]!);
                Assert.Equal(seeded[i].Status, Convert.ToInt32(rows[i][4]));
                Assert.Equal(seeded[i].StatusChangedAt, (DateTime)rows[i][5]!);
                Assert.All(rows[i].Skip(6), Assert.Null);
            }

            // model EF też widzi aplikacje bez CV
            await using var context = CreateContext(cs);
            var applications = await context.JobApplications.AsNoTracking().ToListAsync();
            Assert.Equal(seeded.Count, applications.Count);
            Assert.All(applications, a =>
            {
                Assert.False(a.HasCv);
                Assert.Null(a.CvFileKey);
                Assert.Null(a.CvFileName);
                Assert.Null(a.CvContentType);
                Assert.Null(a.CvSize);
                Assert.Null(a.CvUploadedAt);
            });
        });
    }

    [Fact]
    public async Task Up_NewApplicationsCanBeInsertedWithoutMentioningCvColumns()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (userId, _, _) = await SeedApplicationsAsync(cs);
            await MigrateToAsync(cs, AddApplicationCv);

            var offerId = await ExecuteAsync(cs,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) " +
                "SELECT 'Late', CompanyId, 'D', 'L', 0, UTC_TIMESTAMP(6) FROM JobOffers LIMIT 1");
            var id = await ExecuteAsync(cs,
                "INSERT INTO JobApplications (JobOfferId, UserId, AppliedAt, Status, StatusChangedAt) VALUES (@job, @user, UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6))",
                ("@job", offerId), ("@user", userId));

            var row = Assert.Single(await QueryAsync(cs,
                "SELECT CvFileKey, CvFileName, CvContentType, CvSize, CvUploadedAt FROM JobApplications WHERE Id = @id", ("@id", id)));
            Assert.All(row, Assert.Null);
        });
    }

    [Fact]
    public async Task Up_ColumnsStoreCvMetadataAndEnforceTheirLengths()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (_, _, seeded) = await SeedApplicationsAsync(cs);
            await MigrateToAsync(cs, AddApplicationCv);

            var uploadedAt = new DateTime(2026, 4, 2, 8, 15, 30, 123, DateTimeKind.Utc);
            var longName = new string('ż', 255);
            var longKey = "cv/1/" + new string('k', 295);
            await ExecuteAsync(cs,
                "UPDATE JobApplications SET CvFileKey = @key, CvFileName = @name, CvContentType = @type, CvSize = @size, CvUploadedAt = @at WHERE Id = @id",
                ("@key", longKey), ("@name", longName), ("@type", new string('t', 100)),
                ("@size", 5L * 1024 * 1024 * 1024), ("@at", uploadedAt), ("@id", seeded[0].Id));

            var row = Assert.Single(await QueryAsync(cs,
                "SELECT CvFileKey, CvFileName, CvContentType, CvSize, CvUploadedAt FROM JobApplications WHERE Id = @id", ("@id", seeded[0].Id)));
            Assert.Equal(longKey, row[0]);
            Assert.Equal(longName, row[1]);
            Assert.Equal(100, ((string)row[2]!).Length);
            Assert.Equal(5L * 1024 * 1024 * 1024, Convert.ToInt64(row[3]));
            Assert.Equal(uploadedAt, (DateTime)row[4]!);

            // trzeba zmieścić się w limicie kolumny - za długa wartość nie jest po cichu obcinana
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "UPDATE JobApplications SET CvFileKey = @key WHERE Id = @id", ("@key", new string('k', 301)), ("@id", seeded[1].Id)));
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "UPDATE JobApplications SET CvFileName = @name WHERE Id = @id", ("@name", new string('n', 256)), ("@id", seeded[1].Id)));
            await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(cs,
                "UPDATE JobApplications SET CvContentType = @type WHERE Id = @id", ("@type", new string('t', 101)), ("@id", seeded[1].Id)));
        });
    }

    [Fact]
    public async Task Down_RemovesCvColumnsAndKeepsApplications()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (_, _, seeded) = await SeedApplicationsAsync(cs);
            await MigrateToAsync(cs, AddApplicationCv);
            await ExecuteAsync(cs,
                "UPDATE JobApplications SET CvFileKey = 'cv/1/x.pdf', CvFileName = 'x.pdf', CvContentType = 'application/pdf', CvSize = 10, CvUploadedAt = UTC_TIMESTAMP(6) WHERE Id = @id",
                ("@id", seeded[0].Id));

            await MigrateToAsync(cs, PreviousMigration);

            foreach (var column in CvColumns)
            {
                Assert.False(await ColumnExistsAsync(cs, column), $"Column {column} should be dropped by Down");
            }

            // aplikacje, ich statusy i notatki przetrwały cofnięcie
            var rows = await QueryAsync(cs, "SELECT Id, Note, Status FROM JobApplications ORDER BY Id");
            Assert.Equal(seeded.Select(a => (a.Id, a.Note, a.Status)).ToArray(),
                rows.Select(r => (Convert.ToInt64(r[0]), (string?)r[1], Convert.ToInt32(r[2]))).ToArray());
        });
    }

    [Fact]
    public async Task UpAgainAfterDown_RestoresEmptyCvColumns()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (_, _, seeded) = await SeedApplicationsAsync(cs);
            await MigrateToAsync(cs, AddApplicationCv);
            await ExecuteAsync(cs,
                "UPDATE JobApplications SET CvFileKey = 'cv/1/x.pdf', CvFileName = 'x.pdf', CvContentType = 'application/pdf', CvSize = 10, CvUploadedAt = UTC_TIMESTAMP(6) WHERE Id = @id",
                ("@id", seeded[0].Id));

            await MigrateToAsync(cs, PreviousMigration);
            await using (var context = CreateContext(cs))
            {
                await context.Database.MigrateAsync();
            }

            // dane CV nie są odtwarzane (kolumny wróciły puste), reszta danych bez zmian
            foreach (var column in CvColumns)
            {
                Assert.True(await ColumnExistsAsync(cs, column));
            }

            var rows = await QueryAsync(cs, "SELECT CvFileKey, CvFileName, CvContentType, CvSize, CvUploadedAt FROM JobApplications");
            Assert.Equal(seeded.Count, rows.Count);
            Assert.All(rows, row => Assert.All(row, Assert.Null));
            Assert.Equal(seeded.Count, Convert.ToInt64((await QueryAsync(cs, "SELECT COUNT(*) FROM JobApplications")).Single()[0]));
        });
    }

    [Fact]
    public async Task Migrate_FromInitialSchemaToLatest_LeavesEveryApplicationWithoutCv()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            // pełna ścieżka od pierwszej migracji (stara baza użytkownika) - aplikacje sprzed zmian też nie mają CV
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
            var application = await check.JobApplications.AsNoTracking().SingleAsync();
            Assert.Equal("old", application.Note);
            Assert.False(application.HasCv);
            Assert.False(check.Database.GetPendingMigrations().Any());
        });
    }
}
