using MySqlConnector;

namespace Backend.Tests.Infrastructure;

// Zarządzanie bazą testową: każda fabryka dostaje własną bazę o unikalnej nazwie, usuwaną po testach
public static class TestDatabase
{
    public const string NamePrefix = "JobBoardTest_";

    // Zmienna środowiskowa z connection stringiem do serwera MySQL (nazwa bazy jest ignorowana i zastępowana)
    public const string ConnectionEnvironmentVariable = "JOBBOARD_TEST_CONNECTION";

    // Zwraca connection string do nowej, unikalnej bazy. Serwer pochodzi z JOBBOARD_TEST_CONNECTION,
    // a w razie braku z ConnectionStrings__DefaultConnection (tylko serwer i dane logowania - nazwa bazy jest podmieniana).
    public static string NewConnectionString(out string databaseName)
    {
        var baseConnection =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Port=3306;User=root;Password=devpass;AllowPublicKeyRetrieval=true;";

        databaseName = NamePrefix + Guid.NewGuid().ToString("N");

        var builder = new MySqlConnectionStringBuilder(baseConnection) { Database = databaseName };
        return builder.ConnectionString;
    }

    // Usuwa bazę testową (tylko o nazwie z prefiksem JobBoardTest_ - nigdy JobBoardDb)
    public static async Task DropAsync(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;

        if (!databaseName.StartsWith(NamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to drop database '{databaseName}' - not a test database.");
        }

        builder.Database = string.Empty;
        builder.Pooling = false;

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`";
        await command.ExecuteNonQueryAsync();
    }
}
