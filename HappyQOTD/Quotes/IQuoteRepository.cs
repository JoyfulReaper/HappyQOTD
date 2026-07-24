/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Quotes;

public interface IQuoteRepository
{
    Task<Quote?> GetRandomQuoteAsync(CancellationToken cancellationToken = default);

    Task<Quote> InsertQuoteAsync(
        CreateQuoteRequest quote,
        CancellationToken cancellationToken = default);

    Task<Quote?> GetQuoteAsync(
            long id,
            CancellationToken cancellationToken = default);

    Task<Quote?> GetQuoteOfTheDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteQuoteAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<bool> SetQuoteOfTheDayAsync(
        DateOnly date,
        long? quoteId = null,
        CancellationToken cancellationToken = default);
}