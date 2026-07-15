namespace HappyQOTD.Quotes;

public interface IQuoteRepository
{
    Task<Quote?> GetRandomQuoteAsync(
        CancellationToken cancellationToken = default);

    Task<Quote> InsertQuoteAsync(
        CreateQuoteRequest quote,
        CancellationToken cancellationToken = default);

    Task<Quote?> GetQuoteOfTheDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}