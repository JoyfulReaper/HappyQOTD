namespace HappyQOTD.Events;

public sealed record QOTDApiServedEvent(
    long DurationMilliseconds,
    bool Succeeded);
