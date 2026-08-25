/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Events;

public sealed record QOTDServedEvent(
    string Remote,
    long DurationMilliseconds,
    bool Succeeded,
    string Protocol)
{
    public const string EventName = "happyqotd.qotd.served";

    public const string TcpProtocol = "tcp";
    public const string UdpProtocol = "udp";
}