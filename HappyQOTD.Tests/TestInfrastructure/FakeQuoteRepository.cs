using HappyQOTD.Quotes;

namespace HappyQOTD.Tests.TestInfrastructure;

internal sealed class FakeQuoteRepository : IQuoteRepository
{
    private readonly Func<DateOnly, CancellationToken, Task<Quote?>> _getQuoteOfTheDay;

    public FakeQuoteRepository(Quote? quote)
        : this((_, _) => Task.FromResult<Quote?>(quote))
    {
    }

    public FakeQuoteRepository(
        Func<DateOnly, CancellationToken, Task<Quote?>> getQuoteOfTheDay)
    {
        _getQuoteOfTheDay = getQuoteOfTheDay;
    }

    public Task<Quote?> GetRandomQuoteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Quote?>(null);

    public Task<Quote> InsertQuoteAsync(
        CreateQuoteRequest quote,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Quote?> GetQuoteAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Quote?>(null);

    public Task<Quote?> GetQuoteOfTheDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _getQuoteOfTheDay(date, cancellationToken);

    public Task<bool> DeleteQuoteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> SetQuoteOfTheDayAsync(
        DateOnly date,
        long? quoteId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
