namespace HappyQOTD.Events;

public sealed record QOTDApiServedEvent(
    long DurationMilliseconds,
    string Remote,
    bool Succeeded)
{
    public const string EventType = "happyqotd.api.qotd.served";
}
