using Dapper;
using Microsoft.Data.Sqlite;

namespace HappyQOTD.Quotes;

public sealed class SqliteRepositoryProvider : IQuoteRepository
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

    private const string InsertQuoteSql = """
        INSERT INTO Quotes
        (
            Text,
            Author,
            Source
        )
        VALUES
        (
            @Text,
            @Author,
            @Source
        )
        RETURNING
            Id,
            Text,
            Author,
            Source;
        """;

    private readonly string _connectionString;

    public SqliteRepositoryProvider(string connectionString)
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

    public async Task<Quote> InsertQuoteAsync(
        CreateQuoteRequest quote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quote);

        await using var connection =
            new SqliteConnection(_connectionString);

        var parameters = new
        {
            Text = quote.Text!.Trim(),
            Author = NormalizeOptional(quote.Author),
            Source = NormalizeOptional(quote.Source)
        };

        var command = new CommandDefinition(
            InsertQuoteSql,
            parameters,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<Quote>(command);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}