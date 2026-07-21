namespace HappyQOTD;

public sealed class HappyQOTDOptions
{
    public const string SectionName = "QOTD";
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17;
    public int MaxConcurrentConnections { get; set; } = 64;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public string[] TelemetryIgnoredRemoteAddresses { get; set; } = [];
    public string ApiBaseUrl { get; set; } =
        "http://127.0.0.1:5269";
}
