using Backend.Data;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracje na istniejącej bazie z danymi (własna baza testowa, usuwana na końcu).
// Dane sprzed migracji wstawiane są surowym SQL-em - bieżący model EF ma już kolumny,
// których starszy schemat jeszcze nie zawiera.
[Trait("Category", "Migrations")]
public class MigrationTests
{
    private const string InitialMigration = "20251221163425_InitialCreateMySQL";
    private const string UniqueIndexMigration = "20261104132507_AddUniqueApplicationIndex";

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options);

    private static async Task<List<string>> IndexNamesAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT INDEX_NAME FROM information_schema.STATISTICS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobApplications'";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    // Wykonuje INSERT i zwraca wygenerowane Id
    private static async Task<int> InsertAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
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
        return (int)command.LastInsertedId;
    }

    private static Task<int> InsertJobAsync(string connectionString, string title = "T") =>
        InsertAsync(connectionString,
            "INSERT INTO JobOffers (Title, Company, Description, Location, WorkType, CreatedAt) " +
            "VALUES (@title, 'C', 'D', 'L', 0, UTC_TIMESTAMP(6))",
            ("@title", title));

    private static Task<int> InsertApplicationAsync(string connectionString, int jobId, string userId, DateTime appliedAt) =>
        InsertAsync(connectionString,
            "INSERT INTO JobApplications (JobOfferId, UserId, Note, AppliedAt) VALUES (@job, @user, NULL, @appliedAt)",
            ("@job", jobId), ("@user", userId), ("@appliedAt", appliedAt));

    private static async Task<string> InsertUserAsync(ApplicationDbContext context, string email)
    {
        // surowy SQL - bieżący model ma już kolumnę AspNetUsers.CompanyId (migracja AddCompanies)
        var id = Guid.NewGuid().ToString();
        await InsertAsync(context.Database.GetConnectionString()!,
            "INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, CreatedAt, " +
            "EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
            "VALUES (@id, @email, UPPER(@email), @email, UPPER(@email), UTC_TIMESTAMP(6), 0, 0, 0, 1, 0)",
            ("@id", id), ("@email", email));
        return id;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table";
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    [Fact]
    public async Task AddUniqueApplicationIndex_OnDatabaseWithDuplicates_KeepsOldestApplicationAndIsReversible()
    {
        var connectionString = TestDatabase.NewConnectionString(out _);
        try
        {
            // stan sprzed migracji: duplikaty aplikacji były możliwe
            int jobId;
            string userId;
            var ids = new List<int>();
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(InitialMigration);

                userId = await InsertUserAsync(context, "dup@test.local");
                var otherUserId = await InsertUserAsync(context, "other@test.local");
                jobId = await InsertJobAsync(connectionString);

                foreach (var owner in new[] { userId, userId, otherUserId, userId, otherUserId, userId })
                {
                    ids.Add(await InsertApplicationAsync(connectionString, jobId, owner, DateTime.UtcNow));
                }
            }

            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();

                var remaining = await context.JobApplications.OrderBy(a => a.Id).Select(a => new { a.Id, a.UserId }).ToListAsync();
                Assert.Equal(new[] { ids[0], ids[2] }, remaining.Select(a => a.Id).ToArray());
                Assert.Equal(userId, remaining[0].UserId);

                // indeks unikalny działa, a klucz obcy JobOfferId ma nadal indeks
                context.JobApplications.Add(new JobApplication { JobOfferId = jobId, UserId = userId });
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, Assert.IsType<MySqlException>(exception.InnerException).ErrorCode);
            }

            var indexes = await IndexNamesAsync(connectionString);
            Assert.Contains("IX_JobApplications_JobOfferId_UserId", indexes);
            Assert.DoesNotContain("IX_JobApplications_JobOfferId", indexes);

            // migracja w dół przywraca poprzedni indeks
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(InitialMigration);
            }

            indexes = await IndexNamesAsync(connectionString);
            Assert.Contains("IX_JobApplications_JobOfferId", indexes);
            Assert.DoesNotContain("IX_JobApplications_JobOfferId_UserId", indexes);
        }
        finally
        {
            await MySqlConnection.ClearAllPoolsAsync();
            await TestDatabase.DropAsync(connectionString);
        }
    }

    [Fact]
    public async Task AddRecruitmentWorkflow_OnExistingData_KeepsOffersActiveBackfillsHistoryAndIsReversible()
    {
        var connectionString = TestDatabase.NewConnectionString(out _);
        try
        {
            var appliedAt = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Utc);
            string userId;
            int jobId, applicationId;
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);

                userId = await InsertUserAsync(context, "existing@test.local");
                jobId = await InsertJobAsync(connectionString, "Existing offer");
                applicationId = await InsertApplicationAsync(connectionString, jobId, userId, appliedAt);
            }

            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();

                // istniejąca oferta pozostaje opublikowana i bezterminowa
                var job = await context.JobOffers.AsNoTracking().SingleAsync(j => j.Id == jobId);
                Assert.Equal(JobOfferStatus.Active, job.Status);
                Assert.Null(job.ExpiresAt);

                // istniejąca aplikacja: Submitted od chwili złożenia + jeden wpis w historii autorstwa kandydata
                var application = await context.JobApplications.AsNoTracking()
                    .Include(a => a.StatusHistory)
                    .SingleAsync(a => a.Id == applicationId);
                Assert.Equal(ApplicationStatus.Submitted, application.Status);
                Assert.Equal(appliedAt, application.StatusChangedAt);
                var entry = Assert.Single(application.StatusHistory);
                Assert.Equal(ApplicationStatus.Submitted, entry.Status);
                Assert.Equal(appliedAt, entry.ChangedAt);
                Assert.Equal(userId, entry.ChangedByUserId);
                Assert.Null(entry.Comment);
            }

            // nowa oferta wstawiona bez kolumny Status dostaje domyślnie Active
            // (po migracji AddCompanies oferta wskazuje firmę zamiast kolumny tekstowej Company)
            var defaultedJobId = await InsertAsync(connectionString,
                "INSERT INTO JobOffers (Title, CompanyId, Description, Location, WorkType, CreatedAt) " +
                "VALUES ('Inserted after migration', (SELECT MIN(Id) FROM Companies), 'D', 'L', 0, UTC_TIMESTAMP(6))");
            await using (var context = CreateContext(connectionString))
            {
                Assert.Equal(JobOfferStatus.Active, (await context.JobOffers.AsNoTracking().SingleAsync(j => j.Id == defaultedJobId)).Status);
            }

            // migracja w dół usuwa tabelę historii i nowe kolumny
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);
            }

            Assert.False(await TableExistsAsync(connectionString, "ApplicationStatusHistories"));
        }
        finally
        {
            await MySqlConnection.ClearAllPoolsAsync();
            await TestDatabase.DropAsync(connectionString);
        }
    }
    private static async Task<bool> ColumnExistsAsync(string connectionString, string table, string column)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    [Fact]
    public async Task AddRecruitmentWorkflow_BackfillsOneHistoryEntryPerApplicationAndRespectsForeignKeys()
    {
        var connectionString = TestDatabase.NewConnectionString(out _);
        try
        {
            var firstApplied = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);
            var users = new List<string>();
            var jobs = new List<int>();
            var applications = new List<(int Id, string UserId, int JobId, DateTime AppliedAt)>();
            string recruiterId;
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);

                for (var i = 0; i < 3; i++)
                {
                    users.Add(await InsertUserAsync(context, $"candidate{i}@test.local"));
                    jobs.Add(await InsertJobAsync(connectionString, $"Offer {i}"));
                }

                recruiterId = await InsertUserAsync(context, "recruiter@test.local");

                // każdy kandydat aplikuje na dwie oferty w różnych momentach
                var offset = 0;
                foreach (var user in users)
                {
                    foreach (var job in jobs.Take(2))
                    {
                        var appliedAt = firstApplied.AddHours(offset++);
                        applications.Add((await InsertApplicationAsync(connectionString, job, user, appliedAt), user, job, appliedAt));
                    }
                }
            }

            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();

                // wszystkie oferty (także bez aplikacji) pozostają opublikowane i bezterminowe
                var allJobs = await context.JobOffers.AsNoTracking().ToListAsync();
                Assert.Equal(3, allJobs.Count);
                Assert.All(allJobs, j =>
                {
                    Assert.Equal(JobOfferStatus.Active, j.Status);
                    Assert.Null(j.ExpiresAt);
                });

                // każda aplikacja ma dokładnie jeden wpis historii zgodny z jej danymi
                var loaded = await context.JobApplications.AsNoTracking().Include(a => a.StatusHistory).OrderBy(a => a.Id).ToListAsync();
                Assert.Equal(applications.Count, loaded.Count);
                Assert.Equal(applications.Count, await context.Set<ApplicationStatusHistory>().CountAsync());
                foreach (var expected in applications)
                {
                    var application = loaded.Single(a => a.Id == expected.Id);
                    Assert.Equal(ApplicationStatus.Submitted, application.Status);
                    Assert.Equal(expected.AppliedAt, application.StatusChangedAt);
                    var entry = Assert.Single(application.StatusHistory);
                    Assert.Equal(ApplicationStatus.Submitted, entry.Status);
                    Assert.Equal(expected.AppliedAt, entry.ChangedAt);
                    Assert.Equal(expected.UserId, entry.ChangedByUserId);
                    Assert.Null(entry.Comment);
                }

                // usunięcie autora zmiany zachowuje wpis (ChangedByUserId = NULL)
                var target = applications[0];
                await InsertAsync(connectionString,
                    "INSERT INTO ApplicationStatusHistories (JobApplicationId, Status, ChangedAt, ChangedByUserId, Comment) " +
                    "VALUES (@app, 1, UTC_TIMESTAMP(6), @author, 'checked')",
                    ("@app", target.Id), ("@author", recruiterId));
                context.Users.Remove(await context.Users.SingleAsync(u => u.Id == recruiterId));
                await context.SaveChangesAsync();

                var history = await context.Set<ApplicationStatusHistory>().AsNoTracking()
                    .Where(h => h.JobApplicationId == target.Id).OrderBy(h => h.Id).ToListAsync();
                Assert.Equal(2, history.Count);
                Assert.Equal("checked", history[1].Comment);
                Assert.Null(history[1].ChangedByUserId);
                Assert.Equal(target.UserId, history[0].ChangedByUserId);

                // usunięcie aplikacji usuwa jej historię kaskadowo, pozostałe wpisy zostają
                context.JobApplications.Remove(await context.JobApplications.SingleAsync(a => a.Id == target.Id));
                await context.SaveChangesAsync();
                Assert.Equal(applications.Count - 1, await context.Set<ApplicationStatusHistory>().CountAsync());
            }
        }
        finally
        {
            await MySqlConnection.ClearAllPoolsAsync();
            await TestDatabase.DropAsync(connectionString);
        }
    }

    [Fact]
    public async Task AddRecruitmentWorkflow_DownRemovesNewColumnsAndUpAgainBackfillsAgain()
    {
        var connectionString = TestDatabase.NewConnectionString(out _);
        try
        {
            var appliedAt = new DateTime(2026, 2, 20, 14, 45, 0, DateTimeKind.Utc);
            int applicationId;
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);
                var userId = await InsertUserAsync(context, "roundtrip@test.local");
                var jobId = await InsertJobAsync(connectionString);
                applicationId = await InsertApplicationAsync(connectionString, jobId, userId, appliedAt);
            }

            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();
                await context.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);
            }

            // po cofnięciu zniknęły wszystkie kolumny i tabela dodane przez migrację
            Assert.False(await ColumnExistsAsync(connectionString, "JobOffers", "Status"));
            Assert.False(await ColumnExistsAsync(connectionString, "JobOffers", "ExpiresAt"));
            Assert.False(await ColumnExistsAsync(connectionString, "JobApplications", "Status"));
            Assert.False(await ColumnExistsAsync(connectionString, "JobApplications", "StatusChangedAt"));
            Assert.False(await TableExistsAsync(connectionString, "ApplicationStatusHistories"));

            // ponowne zastosowanie odtwarza stan i historię dla aplikacji, która przetrwała cofnięcie
            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();

                var application = await context.JobApplications.AsNoTracking().Include(a => a.StatusHistory).SingleAsync(a => a.Id == applicationId);
                Assert.Equal(ApplicationStatus.Submitted, application.Status);
                Assert.Equal(appliedAt, application.StatusChangedAt);
                Assert.Equal(appliedAt, Assert.Single(application.StatusHistory).ChangedAt);
            }
        }
        finally
        {
            await MySqlConnection.ClearAllPoolsAsync();
            await TestDatabase.DropAsync(connectionString);
        }
    }
}
