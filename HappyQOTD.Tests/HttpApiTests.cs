using System.Net;
using System.Net.Http.Json;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using HappyQOTD.Tests.TestInfrastructure;

namespace HappyQOTD.Tests;

public sealed class HttpApiTests
{
    private const string AdminKey = "test-admin-key";

    [Fact]
    public async Task Root_ReturnsServiceNameAsPlainText()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("HappyQOTD", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReturnHealthy(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TodayQuote_ReturnsNotFoundWhenNoQuotesExist()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/today");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RandomQuote_ReturnsNotFoundWhenNoQuotesExist()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/random");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithValidQuote_ReturnsCreatedQuote()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", "Author", "Source"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var quote = await response.Content.ReadFromJsonAsync<Quote>();
        Assert.Equal("Created", quote?.Text);
        Assert.Equal("/api/quotes/1", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateQuote_WithInvalidQuote_ReturnsValidationProblem()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"text\"", body);
        Assert.Contains("Quote text is required.", body);
    }

    [Fact]
    public async Task BatchCreate_WithValidQuotes_ReturnsCreatedQuotes()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);
        var requests = new[]
        {
            new CreateQuoteRequest("First", null, null),
            new CreateQuoteRequest("Second", "Author", null)
        };

        var response = await client.PostAsJsonAsync("/api/quotes/batch", requests);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quotes = await response.Content.ReadFromJsonAsync<List<Quote>>();
        Assert.NotNull(quotes);
        Assert.Equal(["First", "Second"], quotes.Select(quote => quote.Text).ToArray());
    }

    [Fact]
    public async Task BatchCreate_WithEmptyPayload_ReturnsBadRequest()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync("/api/quotes/batch", Array.Empty<CreateQuoteRequest>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetTodayQuote_ReturnsOkForExistingQuote()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);
        var created = await CreateQuoteAsync(client, "Pinned");

        var response = await client.PutAsJsonAsync(
            "/api/quotes/today",
            new SetDailyQuoteRequest(created.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var today = await client.GetFromJsonAsync<Quote>("/api/quotes/today");
        Assert.Equal(created.Id, today?.Id);
    }

    [Fact]
    public async Task DeleteQuote_DeletesExistingQuote()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);
        var created = await CreateQuoteAsync(client, "Delete me");

        var response = await client.DeleteAsync($"/api/quotes/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/quotes/random")).StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_ReturnsNotFoundForMissingQuote()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.DeleteAsync("/api/quotes/404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/quotes")]
    [InlineData("POST", "/api/quotes/batch")]
    [InlineData("PUT", "/api/quotes/today")]
    [InlineData("DELETE", "/api/quotes/123")]
    public async Task WriteEndpoints_ReturnServiceUnavailableWhenNoAdminKeyConfigured(
        string method,
        string path)
    {
        using var factory = new TestWebApplicationFactory(adminKey: null);
        using var client = factory.CreateClient();

        var response = await SendWriteRequestAsync(client, method, path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task WriteEndpoints_ReturnUnauthorizedForMissingOrInvalidKey(string? suppliedKey)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        if (suppliedKey is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyEndpointFilter.HeaderName, suppliedKey);
        }

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WriteEndpoints_WithCorrectKeyCanCreateQuote()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task WriteRateLimit_ReturnsTooManyRequestsWhenLimitExceeded()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);

        HttpResponseMessage? response = null;
        for (var i = 0; i < 6; i++)
        {
            response = await client.PostAsJsonAsync(
                "/api/quotes",
                new CreateQuoteRequest($"Quote {i}", null, null));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response?.StatusCode);
    }

    [Fact]
    public async Task TelemetryExceptions_DoNotFailHttpRequest()
    {
        var missionControl = new RecordingMissionControlClient(
            exception: new InvalidOperationException("boom"));
        using var factory = new TestWebApplicationFactory(
            missionControlClient: missionControl);
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ApiTelemetry_RecordsExpectedEventData()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(
            factory.MissionControlClient.Calls,
            call => call.EventType == QuoteAddedEvent.EventType);
        Assert.Equal(QuoteAddedEvent.EventType, call.EventType);
        Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        var payload = Assert.IsType<QuoteAddedEvent>(call.Payload);
        Assert.True(payload.Succeeded);
        Assert.True(payload.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task MissionControlFalseReturn_DoesNotFailHttpRequest()
    {
        using var factory = new TestWebApplicationFactory(
            missionControlClient: new RecordingMissionControlClient(returnValue: false));
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Created", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static HttpClient CreateAdminClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyEndpointFilter.HeaderName, AdminKey);
        return client;
    }

    private static async Task<Quote> CreateQuoteAsync(HttpClient client, string text)
    {
        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest(text, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Quote>())!;
    }

    private static Task<HttpResponseMessage> SendWriteRequestAsync(
        HttpClient client,
        string method,
        string path)
    {
        return method switch
        {
            "POST" when path.EndsWith("/batch", StringComparison.Ordinal) =>
                client.PostAsJsonAsync(path, Array.Empty<CreateQuoteRequest>()),
            "POST" => client.PostAsJsonAsync(path, new CreateQuoteRequest("Text", null, null)),
            "PUT" => client.PutAsJsonAsync(path, new SetDailyQuoteRequest(123)),
            "DELETE" => client.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
        };
    }
}
