namespace HappyQOTD.Quotes;

public interface IQuoteProvider
{
    Task<Quote?> GetRandomQuoteAsync(
        CancellationToken cancellationToken = default);
}