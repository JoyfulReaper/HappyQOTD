using HappyQOTD.Data;
using Microsoft.Data.Sqlite;

namespace HappyQOTD.Tests.TestInfrastructure;

internal sealed class TestQuoteDatabase : IAsyncDisposable
{
    private readonly string _path;

    private TestQuoteDatabase(string path)
    {
        _path = path;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
    }

    public string ConnectionString { get; }

    public static async Task<TestQuoteDatabase> CreateAsync()
    {
        SQLitePCL.Batteries_V2.Init();

        var path = Path.Combine(
            Path.GetTempPath(),
            "HappyQOTD.Tests",
            $"{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var database = new TestQuoteDatabase(path);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = QuoteDatabase.SchemaSql;
            await command.ExecuteNonQueryAsync();
        }

        await database.ClearAsync();
        return database;
    }

    public async Task ClearAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM DailyQuoteSelections;",
            "DELETE FROM Quotes;",
            "DELETE FROM sqlite_sequence WHERE name IN ('Quotes');"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task SetQuoteActiveAsync(long quoteId, bool active)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Quotes SET IsActive = @IsActive WHERE Id = @Id;";
        command.Parameters.AddWithValue("@IsActive", active ? 1 : 0);
        command.Parameters.AddWithValue("@Id", quoteId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountDailySelectionsAsync(long quoteId)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DailyQuoteSelections WHERE QuoteId = @Id;";
        command.Parameters.AddWithValue("@Id", quoteId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
