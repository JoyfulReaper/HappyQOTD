namespace HappyQOTD;

public sealed class HappyQOTDOptions
{
    public const string SectionName = "QOTD";
    public string ListenAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 17;
    public int MaxConcurrentConnections { get; init; } = 64;
    public int RequestTimeoutSeconds { get; init; } = 15;
}
