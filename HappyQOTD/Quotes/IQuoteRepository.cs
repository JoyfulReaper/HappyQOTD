namespace HappyQOTD.Quotes;

public interface IQuoteRepository
{
    Task<Quote?> GetRandomQuoteAsync(
        CancellationToken cancellationToken = default);

    Task<Quote?> InsertQuoteAsync(
        Quote quote,
        CancellationToken cancellationToken = default);
}