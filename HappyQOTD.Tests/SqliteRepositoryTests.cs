using HappyQOTD.Quotes;
using HappyQOTD.Tests.TestInfrastructure;

namespace HappyQOTD.Tests;

public sealed class SqliteRepositoryTests
{
    [Fact]
    public async Task InsertQuoteAsync_CanRetrieveInsertedQuote()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var created = await repository.InsertQuoteAsync(
            new CreateQuoteRequest("A useful quote", "Author", "Source"));

        var retrieved = await repository.GetQuoteAsync(created.Id);

        Assert.Equal(created, retrieved);
    }

    [Fact]
    public async Task InsertQuoteAsync_TrimsTextAuthorAndSource()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var created = await repository.InsertQuoteAsync(
            new CreateQuoteRequest("  Text  ", "  Author  ", "  Source  "));

        Assert.Equal("Text", created.Text);
        Assert.Equal("Author", created.Author);
        Assert.Equal("Source", created.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InsertQuoteAsync_NormalizesEmptyAuthorAndSourceToNull(string value)
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var created = await repository.InsertQuoteAsync(
            new CreateQuoteRequest("Text", value, value));

        Assert.Null(created.Author);
        Assert.Null(created.Source);
    }

    [Fact]
    public async Task GetQuoteAsync_ReturnsNullForMissingQuote()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var quote = await repository.GetQuoteAsync(1234);

        Assert.Null(quote);
    }

    [Fact]
    public async Task DeleteQuoteAsync_DeletesExistingQuoteAndReturnsTrue()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var created = await repository.InsertQuoteAsync(new CreateQuoteRequest("Text", null, null));

        var deleted = await repository.DeleteQuoteAsync((int)created.Id);

        Assert.True(deleted);
        Assert.Null(await repository.GetQuoteAsync(created.Id));
    }

    [Fact]
    public async Task DeleteQuoteAsync_ReturnsFalseForMissingQuote()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var deleted = await repository.DeleteQuoteAsync(404);

        Assert.False(deleted);
    }

    [Fact]
    public async Task GetRandomQuoteAsync_OnlyReturnsActiveQuotes()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var inactive = await repository.InsertQuoteAsync(new CreateQuoteRequest("Inactive", null, null));
        var active = await repository.InsertQuoteAsync(new CreateQuoteRequest("Active", null, null));
        await database.SetQuoteActiveAsync(inactive.Id, active: false);

        for (var i = 0; i < 20; i++)
        {
            var quote = await repository.GetRandomQuoteAsync();
            Assert.Equal(active.Id, quote?.Id);
        }
    }

    [Fact]
    public async Task GetRandomQuoteAsync_ReturnsNullWhenNoActiveQuotesExist()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var inactive = await repository.InsertQuoteAsync(new CreateQuoteRequest("Inactive", null, null));
        await database.SetQuoteActiveAsync(inactive.Id, active: false);

        var quote = await repository.GetRandomQuoteAsync();

        Assert.Null(quote);
    }

    [Fact]
    public async Task SetQuoteOfTheDayAsync_SetsSpecificQuoteForDate()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var created = await repository.InsertQuoteAsync(new CreateQuoteRequest("Today", null, null));
        var date = new DateOnly(2026, 7, 22);

        var updated = await repository.SetQuoteOfTheDayAsync(date, created.Id);
        var quote = await repository.GetQuoteOfTheDayAsync(date);

        Assert.True(updated);
        Assert.Equal(created.Id, quote?.Id);
    }

    [Fact]
    public async Task SetQuoteOfTheDayAsync_RejectsMissingQuote()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);

        var updated = await repository.SetQuoteOfTheDayAsync(
            new DateOnly(2026, 7, 22),
            404);

        Assert.False(updated);
    }

    [Fact]
    public async Task SetQuoteOfTheDayAsync_RejectsInactiveQuote()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var inactive = await repository.InsertQuoteAsync(new CreateQuoteRequest("Inactive", null, null));
        await database.SetQuoteActiveAsync(inactive.Id, active: false);

        var updated = await repository.SetQuoteOfTheDayAsync(
            new DateOnly(2026, 7, 22),
            inactive.Id);

        Assert.False(updated);
    }

    [Fact]
    public async Task GetQuoteOfTheDayAsync_AutomaticallySelectsQuoteForDate()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var created = await repository.InsertQuoteAsync(new CreateQuoteRequest("Auto", null, null));

        var quote = await repository.GetQuoteOfTheDayAsync(new DateOnly(2026, 7, 22));

        Assert.Equal(created.Id, quote?.Id);
    }

    [Fact]
    public async Task GetQuoteOfTheDayAsync_ReturnsSamePersistedQuoteForSameDate()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        await repository.InsertQuoteAsync(new CreateQuoteRequest("First", null, null));
        await repository.InsertQuoteAsync(new CreateQuoteRequest("Second", null, null));
        var date = new DateOnly(2026, 7, 22);

        var first = await repository.GetQuoteOfTheDayAsync(date);
        var second = await repository.GetQuoteOfTheDayAsync(date);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetQuoteOfTheDayAsync_AllowsDifferentDatesToHaveDifferentSelections()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var first = await repository.InsertQuoteAsync(new CreateQuoteRequest("First", null, null));
        var second = await repository.InsertQuoteAsync(new CreateQuoteRequest("Second", null, null));
        var firstDate = new DateOnly(2026, 7, 22);
        var secondDate = new DateOnly(2026, 7, 23);

        await repository.SetQuoteOfTheDayAsync(firstDate, first.Id);
        await repository.SetQuoteOfTheDayAsync(secondDate, second.Id);

        Assert.Equal(first.Id, (await repository.GetQuoteOfTheDayAsync(firstDate))?.Id);
        Assert.Equal(second.Id, (await repository.GetQuoteOfTheDayAsync(secondDate))?.Id);
    }

    [Fact]
    public async Task GetQuoteOfTheDayAsync_ConcurrentCallsForSameDateConvergeOnOneSelection()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        await repository.InsertQuoteAsync(new CreateQuoteRequest("First", null, null));
        await repository.InsertQuoteAsync(new CreateQuoteRequest("Second", null, null));
        var date = new DateOnly(2026, 7, 22);

        var quotes = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => repository.GetQuoteOfTheDayAsync(date)));

        var selectedId = Assert.Single(quotes.Select(quote => quote?.Id).Distinct());
        Assert.True(selectedId > 0);
    }

    [Fact]
    public async Task DeleteQuoteAsync_RemovesRelatedDailySelections()
    {
        await using var database = await TestQuoteDatabase.CreateAsync();
        var repository = new SqliteRepository(database.ConnectionString);
        var created = await repository.InsertQuoteAsync(new CreateQuoteRequest("Text", null, null));
        await repository.SetQuoteOfTheDayAsync(new DateOnly(2026, 7, 22), created.Id);

        await repository.DeleteQuoteAsync((int)created.Id);

        Assert.Equal(0, await database.CountDailySelectionsAsync(created.Id));
    }
}
