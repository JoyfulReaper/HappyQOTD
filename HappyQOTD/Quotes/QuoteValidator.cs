/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Quotes;

public class QuoteValidator
{
    public static Dictionary<string, string[]> ValidateQuote(
    CreateQuoteRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            errors["text"] = ["Quote text is required."];
        }
        else if (request.Text.Trim().Length > 1_000)
        {
            errors["text"] =
                ["Quote text cannot exceed 1,000 characters."];
        }

        if (request.Author?.Trim().Length > 200)
        {
            errors["author"] =
                ["Author cannot exceed 200 characters."];
        }

        if (request.Source?.Trim().Length > 300)
        {
            errors["source"] =
                ["Source cannot exceed 300 characters."];
        }

        return errors;
    }
}
