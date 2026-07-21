namespace HappyQOTD.Events;

public sealed record RandomQuoteServedEvent(
    long DurationMilliseconds,
    string Remote,
    bool Succeeded);
