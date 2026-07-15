namespace HappyQOTD.Events;

public sealed record QOTDServedEvent(
    string Remote,
    long DurationMilliseconds,
    bool Succeeded);