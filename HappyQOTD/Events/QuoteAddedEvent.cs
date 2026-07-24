/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Events;

public sealed record QuoteAddedEvent(
    long DurationMilliseconds,
    string Remote,
    bool Succeeded)
{
    public const string EventType = "happyqotd.api.quote.added";
}
