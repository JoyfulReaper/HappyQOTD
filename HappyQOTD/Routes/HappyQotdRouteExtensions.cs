/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

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

        app.MapGet("/api/quotes/today", HandleQuoteOfTheDayAsync)
            .RequireRateLimiting(QuoteReadRateLimitPolicy);

        app.MapPost("/api/quotes", HandleCreateQuoteAsync)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(QuoteWriteRateLimitPolicy);

        app.MapPost("/api/quotes/batch", HandleCreateBatchQuotesAsync)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(QuoteWriteRateLimitPolicy);

        app.MapGet("/api/quotes/random", HandleRandomQuoteAsync)
            .RequireRateLimiting(QuoteReadRateLimitPolicy);

        app.MapPut("/api/quotes/today", HandleSetTodayQuoteAsync)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(QuoteWriteRateLimitPolicy);

        app.MapDelete("/api/quotes/{id:int}", HandleDeleteQuoteAsync)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(QuoteWriteRateLimitPolicy);

        return app;
    }

    private static async Task<IResult> HandleDeleteQuoteAsync(
        int id,
        IQuoteRepository repository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var quote = await repository.GetQuoteAsync(id, cancellationToken);
        var deleted = await repository.DeleteQuoteAsync(id, cancellationToken);

        stopwatch.Stop();
        var remoteIp = GetRemoteIpAddress(httpContext);


        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: QuoteDeletedEvent.EventName,
            payload: new QuoteDeletedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                quoteId: quote?.Id ?? 0,
                quoteText: quote?.Text ?? string.Empty,
                Remote: remoteIp,
                Succeeded: deleted),
            payloadTypeInfo: QOTDJsonContext.Default.QuoteDeletedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = $"Quote with ID {id} was not found." });
    }

    private static string GetRemoteIpAddress(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async Task<IResult> HandleSetTodayQuoteAsync(
        SetDailyQuoteRequest request,
        IQuoteRepository repository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var updated = await repository.SetQuoteOfTheDayAsync(today, request.QuoteId, cancellationToken);

        if (!updated)
        {
            return Results.NotFound(new { error = $"Active quote with ID {request.QuoteId} was not found." });
        }

        return Results.Ok(new { message = $"Quote of the day for {today} updated successfully.", quoteId = request.QuoteId });
    }

    private static async Task<IResult> HandleQuoteOfTheDayAsync(
        IQuoteRepository quoteRepository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var quote = await quoteRepository.GetQuoteOfTheDayAsync(today, cancellationToken);

        stopwatch.Stop();
        var remoteIp = GetRemoteIpAddress(httpContext);
        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: QOTDApiServedEvent.EventType,
            payload: new QOTDApiServedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Remote: remoteIp,
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
        HttpContext httpContext,
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

        var created = await repository.InsertQuoteAsync(request, cancellationToken);

        stopwatch.Stop();
        var remote = GetRemoteIpAddress(httpContext);
        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: QuoteAddedEvent.EventType,
            payload: new QuoteAddedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Remote: remote,
                Succeeded: true),
            payloadTypeInfo: QOTDJsonContext.Default.QuoteAddedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return Results.Created($"/api/quotes/{created.Id}", created);
    }

    private static async Task<IResult> HandleCreateBatchQuotesAsync(
        List<CreateQuoteRequest> requests,
        IQuoteRepository repository,
        IMissionControlClient missionControlClient,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken
    )
    {
        if (requests is null || requests.Count == 0)
        {
            return Results.BadRequest(new { error = "Request payload cannot be empty" });
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var allErrors = new Dictionary<string, string[]>();
        for (int i = 0; i < requests.Count; i++)
        {
            var errors = QuoteValidator.ValidateQuote(requests[i]);
            foreach (var error in errors)
            {
                allErrors[$"[{i}].{error.Key}"] = error.Value;
            }
        }

        if (allErrors.Count > 0)
        {
            return Results.ValidationProblem(allErrors);
        }

        var createdQuotes = new List<Quote>();
        foreach (var request in requests)
        {
            var created = await repository.InsertQuoteAsync(request, cancellationToken);
            createdQuotes.Add(created);
        }

        stopwatch.Stop();

        var remote = GetRemoteIpAddress(httpContext);
        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: "happyqotd.api.quotes.batch_added",
            payload: new QuoteAddedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Remote: remote,
                Succeeded: true),
            payloadTypeInfo: QOTDJsonContext.Default.QuoteAddedEvent,
            occurredAt,
            correlationId,
            cancellationToken);

        return Results.Ok(createdQuotes);
    }

    private static async Task<IResult> HandleRandomQuoteAsync(
        IQuoteRepository quoteRepository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var quote = await quoteRepository.GetRandomQuoteAsync(cancellationToken);

        stopwatch.Stop();
        var remote = GetRemoteIpAddress(httpContext);
        await TryPublishTelemetryAsync(
            missionControlClient,
            logger,
            eventType: "happyqotd.api.randomquote.served",
            payload: new RandomQuoteServedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Remote: remote,
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
