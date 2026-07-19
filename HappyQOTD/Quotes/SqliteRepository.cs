using Microsoft.Data.Sqlite;

namespace HappyQOTD.Quotes;

public sealed class SqliteRepository : IQuoteRepository
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

    private const string CreateDailySelectionSql = """
        INSERT OR IGNORE INTO DailyQuoteSelections
        (
            SelectionDate,
            QuoteId
        )
        SELECT
            @SelectionDate,
            Id
        FROM Quotes
        WHERE IsActive = 1
        ORDER BY RANDOM()
        LIMIT 1;
        """;

    private const string GetDailyQuoteSql = """
        SELECT
            quote.Id,
            quote.Text,
            quote.Author,
            quote.Source
        FROM DailyQuoteSelections AS selection
        INNER JOIN Quotes AS quote
            ON quote.Id = selection.QuoteId
        WHERE selection.SelectionDate = @SelectionDate
        LIMIT 1;
        """;

    private readonly string _connectionString;

    public SqliteRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task<Quote?> GetQuoteOfTheDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        var selectionDate = date.ToString("yyyy-MM-dd");

        using var createCommand = connection.CreateCommand();
        createCommand.CommandText = CreateDailySelectionSql;
        AddParameter(createCommand, "@SelectionDate", selectionDate);

        await createCommand.ExecuteNonQueryAsync(cancellationToken);

        using var getCommand = connection.CreateCommand();
        getCommand.CommandText = GetDailyQuoteSql;
        AddParameter(getCommand, "@SelectionDate", selectionDate);

        return await QuerySingleOrDefaultQuoteAsync(
            getCommand,
            cancellationToken);
    }

    public async Task<Quote?> GetRandomQuoteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = RandomQuoteSql;

        return await QuerySingleOrDefaultQuoteAsync(
            command,
            cancellationToken);
    }

    public async Task<Quote> InsertQuoteAsync(
        CreateQuoteRequest quote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quote);

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = InsertQuoteSql;
        AddParameter(command, "@Text", quote.Text!.Trim());
        AddParameter(command, "@Author", NormalizeOptional(quote.Author));
        AddParameter(command, "@Source", NormalizeOptional(quote.Source));

        return await QuerySingleQuoteAsync(
            command,
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddParameter(
        SqliteCommand command,
        string name,
        string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value is null
            ? DBNull.Value
            : value;
        command.Parameters.Add(parameter);
    }

    private static async Task<Quote?> QuerySingleOrDefaultQuoteAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var quote = MapQuote(reader);

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Sequence contains more than one element.");
        }

        return quote;
    }

    private static async Task<Quote> QuerySingleQuoteAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Sequence contains no elements.");
        }

        var quote = MapQuote(reader);

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Sequence contains more than one element.");
        }

        return quote;
    }

    private static Quote MapQuote(SqliteDataReader reader)
    {
        var idOrdinal = reader.GetOrdinal("Id");
        var textOrdinal = reader.GetOrdinal("Text");
        var authorOrdinal = reader.GetOrdinal("Author");
        var sourceOrdinal = reader.GetOrdinal("Source");

        return new Quote(
            Id: reader.GetInt64(idOrdinal),
            Text: reader.GetString(textOrdinal),
            Author: reader.IsDBNull(authorOrdinal)
                ? null
                : reader.GetString(authorOrdinal),
            Source: reader.IsDBNull(sourceOrdinal)
                ? null
                : reader.GetString(sourceOrdinal));
    }
}
