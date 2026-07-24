/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Quotes;

public sealed record Quote(
    long Id,
    string Text,
    string? Author = null,
    string? Source = null);