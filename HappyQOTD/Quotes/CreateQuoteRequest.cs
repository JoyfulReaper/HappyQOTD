/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Quotes;

public sealed record CreateQuoteRequest(
    string? Text,
    string? Author,
    string? Source);