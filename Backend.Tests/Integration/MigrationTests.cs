using Backend.Data;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Backend.Tests.Integration;

// Migracje na istniejącej bazie z danymi (własna baza testowa, usuwana na końcu)
public class MigrationTests
{
    private const string InitialMigration = "20251221163425_InitialCreateMySQL";

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

                var user = new ApplicationUser { UserName = "dup@test.local", Email = "dup@test.local" };
                var otherUser = new ApplicationUser { UserName = "other@test.local", Email = "other@test.local" };
                var job = new JobOffer { Title = "T", Company = "C", Description = "D", Location = "L" };
                context.AddRange(user, otherUser, job);
                await context.SaveChangesAsync();
                jobId = job.Id;
                userId = user.Id;

                foreach (var owner in new[] { user, user, otherUser, user, otherUser, user })
                {
                    var application = new JobApplication { JobOfferId = job.Id, UserId = owner.Id };
                    context.JobApplications.Add(application);
                    await context.SaveChangesAsync();
                    ids.Add(application.Id);
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
}
