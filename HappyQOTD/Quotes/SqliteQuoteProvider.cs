using Dapper;
using Microsoft.Data.Sqlite;

namespace HappyQOTD.Quotes;

public sealed class SqliteQuoteProvider : IQuoteProvider
{
    private const string RandomQuoteSql = """
        SELECT
            Id,
            Text,
            Author,
            Source
        FROM Quotes
        WHERE IsActive = 1
        ORDER BY RANDOM()
        LIMIT 1;
        """;

    private readonly string _connectionString;

    public SqliteQuoteProvider(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task<Quote?> GetRandomQuoteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        var command = new CommandDefinition(
            RandomQuoteSql,
            cancellationToken: cancellationToken);

        return await connection
            .QuerySingleOrDefaultAsync<Quote>(command);
    }
}