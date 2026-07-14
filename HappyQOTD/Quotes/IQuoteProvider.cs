namespace HappyQOTD.Quotes;

public interface IQuoteProvider
{
    Quote? GetRandomQuote();
}