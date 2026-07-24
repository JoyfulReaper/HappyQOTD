/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Events;

public sealed record QOTDServedEvent(
    string Remote,
    long DurationMilliseconds,
    bool Succeeded)
{
    public const string EventName = "happyqotd.qotd.served";
}