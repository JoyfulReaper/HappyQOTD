namespace HappyQOTD.Quotes;

public sealed record CreateQuoteRequest(
    string? Text,
    string? Author,
    string? Source);