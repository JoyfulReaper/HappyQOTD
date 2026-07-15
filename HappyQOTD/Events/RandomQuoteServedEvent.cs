namespace HappyQOTD.Events;

public sealed record RandomQuoteServedEvent(
    long DurationMilliseconds,
    bool Succeeded);
