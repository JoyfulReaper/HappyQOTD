// Currently unused

using System.Net;

namespace HappyQOTD.Quotes;

public sealed class QuoteApiClient(HttpClient httpClient)
{
    public async Task<Quote?> GetQuoteOfTheDayAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            "api/quotes/today",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Quote>(QuoteJsonContext.Default.Quote, cancellationToken)
            ?? throw new InvalidDataException("The quote API returned an empty response.");
    }
}
