namespace HappyQOTD.Quotes;

public sealed record Quote(
    int Id,
    string Text,
    string? Author = null,
    string? Source = null);