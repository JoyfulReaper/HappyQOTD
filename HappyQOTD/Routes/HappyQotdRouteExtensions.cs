using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using JoyfulReaperLib.MissionControl;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;

namespace HappyQOTD;

public static class HappyQotdRouteExtensions
{
    private const string QuoteWriteRateLimitPolicy = "quote-write";
    private const string QuoteReadRateLimitPolicy = "quote-read";

    public static WebApplication MapHappyQotdEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/", () => "HappyQOTD");

        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });

        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            });

        app.MapGet(
                "/api/quotes/today",
                HandleQuoteOfTheDayAsync)
            .RequireRateLimiting(QuoteReadRateLimitPolicy);

        app.MapPost(
                "/api/quotes",
                HandleCreateQuoteAsync)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(QuoteWriteRateLimitPolicy);

        app.MapGet(
                "/api/quotes/random",
                HandleRandomQuoteAsync)
            .RequireRateLimiting(QuoteReadRateLimitPolicy);

        return app;
    }

    private static async Task<IResult> HandleQuoteOfTheDayAsync(
        IQuoteRepository quoteRepository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var quote = await quoteRepository.GetQuoteOfTheDayAsync(
            today,
            cancellationToken);

        stopwatch.Stop();

        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: "happyqotd.api.qotd.served",
            payload: new QOTDApiServedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Succeeded: quote is not null),
            payloadTypeInfo: QOTDJsonContext.Default.QOTDApiServedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return quote is null
            ? Results.NotFound()
            : Results.Ok(quote);
    }

    private static async Task<IResult> HandleCreateQuoteAsync(
        CreateQuoteRequest request,
        IQuoteRepository repository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var errors = QuoteValidator.ValidateQuote(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var created = await repository.InsertQuoteAsync(
            request,
            cancellationToken);

        stopwatch.Stop();

        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: "happyqotd.api.quote.added",
            payload: new QuoteAddedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Succeeded: true),
            payloadTypeInfo: QOTDJsonContext.Default.QuoteAddedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return Results.Created($"/api/quotes/{created.Id}", created);
    }

    private static async Task<IResult> HandleRandomQuoteAsync(
        IQuoteRepository quoteRepository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var quote = await quoteRepository.GetRandomQuoteAsync(cancellationToken);

        stopwatch.Stop();

        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: "happyqotd.api.randomquote.served",
            payload: new RandomQuoteServedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Succeeded: quote is not null),
            payloadTypeInfo: QOTDJsonContext.Default.RandomQuoteServedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return quote is null
            ? Results.NotFound()
            : Results.Ok(quote);
    }

    private static async Task TryPublishTelemetryAsync<TEvent>(
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        string eventType,
        TEvent payload,
        JsonTypeInfo<TEvent> payloadTypeInfo,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken)
        where TEvent : class
    {
        try
        {
            await missionControlClient.TryPublishAsync<TEvent>(
                eventType: eventType,
                payload: payload,
                payloadTypeInfo: payloadTypeInfo,
                occurredAt: occurredAt,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish telemetry for QOTD API random quote client.");
        }
    }
}
