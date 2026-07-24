/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Events;

public sealed record QOTDServiceStartedEvent(string ListenAddress)
{
    public const string EventName = "happyqotd.service.started";
}