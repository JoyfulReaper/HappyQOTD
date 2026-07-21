namespace HappyQOTD.Events;

public sealed record QOTDServiceStartedEvent(
    string ListenAddress)
{
    public const string EventName = "happyqotd.service.started";
}