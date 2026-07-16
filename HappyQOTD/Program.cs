using HappyQOTD;
using HappyQOTD.Data;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using JoyfulReaperLib.MissionControl;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

const string QuoteWriteRateLimitPolicy = "quote-write";
const string QuoteReadRateLimitPolicy = "quote-read";
const string BrowserClientCorsPolicy = "browser-client";

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        BrowserClientCorsPolicy,
        policy =>
        {
            policy.WithOrigins(
                "https://kgivler.com",
                "https://www.kgivler.com")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(
        QuoteWriteRateLimitPolicy,
        limiter =>
        {
            limiter.PermitLimit = 5;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
            limiter.QueueProcessingOrder =
                QueueProcessingOrder.OldestFirst;
            limiter.AutoReplenishment = true;
        });

    options.AddPolicy(
        QuoteReadRateLimitPolicy,
        httpContext =>
        {
            var remoteAddress =
                httpContext.Connection.RemoteIpAddress;

            if (remoteAddress is not null &&
                IPAddress.IsLoopback(remoteAddress))
            {
                return RateLimitPartition.GetNoLimiter(
                    "loopback");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    remoteAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder =
                        QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        });
});

builder.Services.Configure<HappyQOTDOptions>(
    builder.Configuration.GetSection(
        HappyQOTDOptions.SectionName));

var quoteConnectionString = QuoteDatabase.Initialize();
builder.Services.AddSingleton<IQuoteRepository>(
    _ => new SqliteRepository(quoteConnectionString));
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .Add(
        new HealthCheckRegistration(
            "sqlite",
            _ => new SqliteHealthCheck(quoteConnectionString),
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]));

builder.Services.Configure<QotdSecurityOptions>(
    builder.Configuration.GetSection(
        QotdSecurityOptions.SectionName));

builder.Services.AddMissionControlClient(
    builder.Configuration.GetSection(
        MissionControlClientOptions.SectionName));

builder.Services.AddScoped<ApiKeyEndpointFilter>();
builder.Services.AddHostedService<HappyQOTDWorker>();

builder.Services.AddProblemDetails();


var app = builder.Build();

// Cloudflare Tunnel Header Matching Middleware
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Loopback);
forwardedOptions.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);

// Parse Cloudflare's specific schema declaration
app.Use((context, next) =>
{
    if (context.Request.Headers.TryGetValue("CF-Visitor", out var cfVisitor))
    {
        if (cfVisitor.ToString().Contains("\"scheme\":\"https\""))
        {
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        }
    }
    return next();
});

app.UseForwardedHeaders(forwardedOptions);
app.UseCors(BrowserClientCorsPolicy);
app.UseRateLimiter();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "HappyQOTD");
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration =>
            registration.Tags.Contains("live")
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration =>
            registration.Tags.Contains("ready")
    });

app.MapGet(
    "/api/quotes/today",
    async (
        IQuoteRepository quoteRepository,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var quote = await quoteRepository.GetQuoteOfTheDayAsync(
            today,
            cancellationToken);

        stopwatch.Stop();
        try
        {
            await missionControlClient.TryPublishAsync<QOTDApiServedEvent>(
                eventType: "happyqotd.api.qotd.served",
                payload: new QOTDApiServedEvent(
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                    Succeeded: quote is not null
                ),
                occurredAt,
                correlationId,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish telemetry for QOTD API random quote client.");
        }

        return quote is null
            ? Results.NotFound()
            : Results.Ok(quote);
    }).RequireRateLimiting(QuoteReadRateLimitPolicy);

app.MapPost(
        "/api/quotes",
        async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            IMissionControlClient missionControlClient,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
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
            try
            {
                await missionControlClient.TryPublishAsync<QuoteAddedEvent>(
                    eventType: "happyqotd.api.quote.added",
                    payload: new QuoteAddedEvent(
                        DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                        Succeeded: true
                    ),
                    occurredAt,
                    correlationId,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to publish telemetry for QOTD API random quote client.");
            }

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        })
    .AddEndpointFilter<ApiKeyEndpointFilter>()
    .RequireRateLimiting(QuoteWriteRateLimitPolicy);

app.MapGet(
    "/api/quotes/random",
    async (IQuoteRepository quoteRepo,
        IMissionControlClient missionControlClient,
        ILogger<Program> logger,
CancellationToken cancellationToken) =>
{
    var occurredAt = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var correlationId = Guid.NewGuid().ToString("N");

    var quote = await quoteRepo.GetRandomQuoteAsync(cancellationToken);

    stopwatch.Stop();
    try
    {
        await missionControlClient.TryPublishAsync<RandomQuoteServedEvent>(
            eventType: "happyqotd.api.randomquote.served",
            payload: new RandomQuoteServedEvent(
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Succeeded: quote is not null
            ),
            occurredAt,
            correlationId,
            cancellationToken
        );
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            ex,
            "Failed to publish telemetry for QOTD API random quote client.");
    }


    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
}).RequireRateLimiting(QuoteReadRateLimitPolicy);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Run();
