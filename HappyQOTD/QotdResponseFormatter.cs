/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Quotes;

namespace HappyQOTD;

internal static class QotdResponseFormatter
{
    public static string FormatQuote(
        Quote quote)
    {
        string? attribution = quote.Author;

        if (!string.IsNullOrWhiteSpace(quote.Source))
        {
            attribution = string.IsNullOrWhiteSpace(attribution)
                ? quote.Source
                : $"{attribution}, {quote.Source}";
        }

        return string.IsNullOrWhiteSpace(attribution)
            ? $"{quote.Text}\r\n"
            : $"{quote.Text}\r\n-- {attribution}\r\n";
    }

    public static string ApplyLengthPolicy(
        string response,
        HappyQOTDOptions options)
    {
        if (!options.TruncateQuoteResponses)
        {
            return response;
        }

        int maximumCharacters =
            Math.Max(1, options.MaximumQuoteResponseCharacters);

        if (response.Length <= maximumCharacters)
        {
            return response;
        }

        if (maximumCharacters <= 2)
        {
            return response[..maximumCharacters];
        }

        string trimmed = response[..(maximumCharacters - 2)]
            .TrimEnd('\r', '\n');

        return $"{trimmed}\r\n";
    }
}