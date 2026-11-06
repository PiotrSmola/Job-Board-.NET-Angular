using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracja AddUserAccount na bazie z danymi: istniejące konta dostają puste pola profilu i włączone powiadomienia,
// nowe tabele (dokumenty profilu, zapisane oferty, outbox e-maili, klucze Data Protection) z kluczami obcymi
// kaskadowymi; Down usuwa je i nie rusza reszty danych. Dane sprzed migracji wstawiane są surowym SQL-em.
[Trait("Category", "Migrations")]
public class AddUserAccountMigrationTests
{
    private const string PreviousMigration = "20261106103645_AddApplicationAttachments";
    private const string AddUserAccount = "20261106142123_AddUserAccount";

    private static readonly string[] NewTables = ["DataProtectionKeys", "EmailMessages", "ProfileDocuments", "SavedJobs"];

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

    private static async Task<List<string>> TablesAsync(string connectionString) =>
        (await QueryAsync(connectionString, "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"))
        .Select(r => (string)r[0]!).ToList();

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

    // konto i oferta na schemacie sprzed migracji
    private static async Task<(string UserId, long JobId)> SeedAsync(string connectionString)
    {
        var userId = Guid.NewGuid().ToString();
        await ExecuteAsync(connectionString,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, FirstName, CreatedAt, " +
            "EmailConfirmed, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, 'old@test.local', 'OLD@TEST.LOCAL', 'old@test.local', 'OLD@TEST.LOCAL', 'Old', UTC_TIMESTAMP(6), 1, '+48 1', 0, 0, 1, 0)",
            ("@id", userId));
        var companyId = await ExecuteAsync(connectionString,
            "INSERT INTO Companies (Name, Status, CreatedAt) VALUES ('Account Company', 1, UTC_TIMESTAMP(6))");
        var jobId = await ExecuteAsync(connectionString,
            "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) VALUES ('T', @company, 'D', 'L', 0, UTC_TIMESTAMP(6))",
            ("@company", companyId));
        return (userId, jobId);
    }

    [Fact]
    public async Task Up_KeepsExistingAccountsWithEmptyProfileFieldsAndNotificationsEnabled()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (userId, _) = await SeedAsync(cs);

            await MigrateToAsync(cs, AddUserAccount);

            var row = Assert.Single(await QueryAsync(cs,
                "SELECT FirstName, PhoneNumber, Headline, Location, LinkedInUrl, About, NotifyApplicationStatusChanged, " +
                "NotifyNewApplication, NotifyCompanyReview FROM AspNetUsers WHERE Id = @id", ("@id", userId)));
            Assert.Equal("Old", row[0]);
            Assert.Equal("+48 1", row[1]);
            Assert.All(row[2..6], value => Assert.Null(value));
            Assert.All(row[6..], value => Assert.True(Convert.ToBoolean(value)));

            // EF odczytuje konto z nowymi polami
            await using var context = CreateContext(cs);
            var user = await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.True(user.NotifyApplicationStatusChanged && user.NotifyNewApplication && user.NotifyCompanyReview);
        });
    }

    [Fact]
    public async Task Up_CreatesTheTablesWithExpectedColumnsIndexesAndCascadingForeignKeys()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            Assert.Empty((await TablesAsync(cs)).Intersect(NewTables));

            await MigrateToAsync(cs, AddUserAccount);
            Assert.Equal(NewTables, (await TablesAsync(cs)).Intersect(NewTables).Order());

            var columns = (await QueryAsync(cs,
                    "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('ProfileDocuments', 'SavedJobs', 'EmailMessages')"))
                .ToDictionary(r => $"{r[0]}.{r[1]}", r => ((string)r[2]!, (string)r[3]!));
            Assert.Equal(("varchar(300)", "NO"), columns["ProfileDocuments.FileKey"]);
            Assert.Equal(("varchar(255)", "NO"), columns["ProfileDocuments.FileName"]);
            Assert.Equal(("bigint", "NO"), columns["ProfileDocuments.Size"]);
            Assert.Equal(("datetime(6)", "NO"), columns["SavedJobs.SavedAt"]);
            Assert.Equal(("bigint", "NO"), columns["EmailMessages.Id"]);
            Assert.Equal(("longtext", "YES"), columns["EmailMessages.HtmlBody"]);
            Assert.Equal(("varchar(500)", "YES"), columns["EmailMessages.LastError"]);
            Assert.Equal(("datetime(6)", "NO"), columns["EmailMessages.NextAttemptAt"]);

            var indexes = (await QueryAsync(cs,
                    "SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE FROM information_schema.STATISTICS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('ProfileDocuments', 'SavedJobs', 'EmailMessages') " +
                    "GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE"))
                .ToDictionary(r => $"{r[0]}.{r[1]}", r => Convert.ToInt32(r[2]));
            Assert.Equal(0, indexes["ProfileDocuments.IX_ProfileDocuments_FileKey"]);
            Assert.Equal(1, indexes["ProfileDocuments.IX_ProfileDocuments_UserId"]);
            Assert.Equal(1, indexes["SavedJobs.IX_SavedJobs_UserId_SavedAt"]);
            Assert.Equal(1, indexes["SavedJobs.IX_SavedJobs_JobOfferId"]);
            Assert.Equal(1, indexes["EmailMessages.IX_EmailMessages_Status_NextAttemptAt"]);
            Assert.Equal(1, indexes["EmailMessages.IX_EmailMessages_UserId_Type_CreatedAt"]);

            var foreignKeys = (await QueryAsync(cs,
                    "SELECT TABLE_NAME, REFERENCED_TABLE_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                    "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME IN ('ProfileDocuments', 'SavedJobs', 'EmailMessages')"))
                .Select(r => $"{r[0]}->{r[1]}:{r[2]}").Order().ToArray();
            Assert.Equal(
                ["EmailMessages->AspNetUsers:CASCADE", "ProfileDocuments->AspNetUsers:CASCADE",
                 "SavedJobs->AspNetUsers:CASCADE", "SavedJobs->JobOffers:CASCADE"],
                foreignKeys);
        });
    }

    [Fact]
    public async Task DeletingAUserOrAnOffer_CascadesToTheNewTables()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, AddUserAccount);
            var (userId, jobId) = await SeedAsync(cs);
            await ExecuteAsync(cs,
                "INSERT INTO ProfileDocuments (UserId, Kind, FileKey, FileName, ContentType, Size, UploadedAt) " +
                "VALUES (@user, 0, 'profile-documents/x/a.pdf', 'cv.pdf', 'application/pdf', 10, UTC_TIMESTAMP(6))", ("@user", userId));
            await ExecuteAsync(cs, "INSERT INTO SavedJobs (UserId, JobOfferId, SavedAt) VALUES (@user, @job, UTC_TIMESTAMP(6))",
                ("@user", userId), ("@job", jobId));
            await ExecuteAsync(cs,
                "INSERT INTO EmailMessages (UserId, Type, ToAddress, Subject, Status, Attempts, CreatedAt, NextAttemptAt) " +
                "VALUES (@user, 0, 'old@test.local', 'S', 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))", ("@user", userId));

            await ExecuteAsync(cs, "DELETE FROM JobOffers WHERE Id = @job", ("@job", jobId));
            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs, "SELECT COUNT(*) FROM SavedJobs")).Single()[0]));

            await ExecuteAsync(cs, "DELETE FROM AspNetUsers WHERE Id = @user", ("@user", userId));
            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs, "SELECT COUNT(*) FROM ProfileDocuments")).Single()[0]));
            Assert.Equal(0L, Convert.ToInt64((await QueryAsync(cs, "SELECT COUNT(*) FROM EmailMessages")).Single()[0]));
        });
    }

    [Fact]
    public async Task Down_RemovesTheNewTablesAndColumnsAndKeepsTheData()
    {
        await RunWithDatabaseAsync(async cs =>
        {
            await MigrateToAsync(cs, PreviousMigration);
            var (userId, jobId) = await SeedAsync(cs);
            await MigrateToAsync(cs, AddUserAccount);

            await MigrateToAsync(cs, PreviousMigration);

            Assert.Empty((await TablesAsync(cs)).Intersect(NewTables));
            var accountColumns = await QueryAsync(cs,
                "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AspNetUsers' " +
                "AND COLUMN_NAME IN ('Headline', 'Location', 'LinkedInUrl', 'About', 'NotifyApplicationStatusChanged', " +
                "'NotifyNewApplication', 'NotifyCompanyReview')");
            Assert.Empty(accountColumns);
            Assert.Single(await QueryAsync(cs, "SELECT Id FROM AspNetUsers WHERE Id = @id AND FirstName = 'Old'", ("@id", userId)));
            Assert.Single(await QueryAsync(cs, "SELECT Id FROM JobOffers WHERE Id = @id", ("@id", jobId)));

            // ponowne Up po Down
            await using var context = CreateContext(cs);
            await context.Database.MigrateAsync();
            Assert.Equal(NewTables, (await TablesAsync(cs)).Intersect(NewTables).Order());
            // migracja jest zastosowana (późniejsze migracje mogą być po niej)
            Assert.Contains(AddUserAccount, context.Database.GetAppliedMigrations());
        });
    }
}
