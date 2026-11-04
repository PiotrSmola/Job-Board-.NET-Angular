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
        // tabela AspNetUsers nie zmienia się w migracjach - można użyć bieżącego modelu
        var user = new ApplicationUser { UserName = email, Email = email };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
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
            var defaultedJobId = await InsertJobAsync(connectionString, "Inserted after migration");
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
}
