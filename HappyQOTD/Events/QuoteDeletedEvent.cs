/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Events;

public sealed record QuoteDeletedEvent(
    string Remote,
    long DurationMilliseconds,
    long quoteId,
    string quoteText,
    bool Succeeded)
{
    public const string EventName = "happyqotd.api.quote.deleted";
}