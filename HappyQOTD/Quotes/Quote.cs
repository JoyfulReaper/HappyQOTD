namespace HappyQOTD.Quotes;

public sealed record Quote(
    long Id,
    string Text,
    string? Author = null,
    string? Source = null);