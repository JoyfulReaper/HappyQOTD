/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using JoyfulReaperLib.TcpServer;

namespace HappyQOTD;

public sealed class HappyQOTDOptions : ITcpServerOptions
{
    public const string SectionName = "QOTD";

    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17;
    public bool EnableTcpServer { get; set; } = true;
    public int MaxConcurrentConnections { get; set; } = 64;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public string? QuoteConnectionString { get; set; }
    public string[] TelemetryIgnoredRemoteAddresses { get; set; } = [];
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5269";

    ConnectionLimitBehavior ITcpServerOptions.ConnectionLimitBehavior =>
        ConnectionLimitBehavior.Wait;
}
