namespace HappyQOTD.Quotes;

public sealed class InMemoryQuoteProvider : IQuoteProvider
{
    private static readonly Quote[] Quotes =
    [
        new(
            1,
            "The best code is no code at all.",
            "Jeff Atwood"),

        new(
            2,
            "Programs must be written for people to read.",
            "Harold Abelson"),

        new(
            3,
            "Any sufficiently advanced bug is indistinguishable from a feature.",
            "Unknown")
    ];

    public Quote? GetRandomQuote()
    {
        return Quotes.Length == 0
            ? null
            : Quotes[Random.Shared.Next(Quotes.Length)];
    }
}