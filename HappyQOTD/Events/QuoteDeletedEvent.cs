namespace HappyQOTD.Events;

public sealed record QuoteDeletedEvent(
    string Remote,
    long DurationMilliseconds,
    long quoteId,
    string quoteText,
    bool Succeeded)
{
    public const string EventName = "happyqotd.api.quote.deleted";
}