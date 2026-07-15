namespace HappyQOTD.Events;

public sealed record QuoteAddedEvent(
    long DurationMilliseconds,
    bool Succeeded);
