using HappyQOTD;
using HappyQOTD.Data;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using JoyfulReaperLib.MissionControl;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

const string QuoteWriteRateLimitPolicy = "quote-write";
const string QuoteReadRateLimitPolicy = "quote-read";

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
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder =
                        QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                }));
});

var quoteConnectionString = QuoteDatabase.Initialize();
builder.Services.AddSingleton<IQuoteRepository>(
    _ => new SqliteRepository(quoteConnectionString));

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
app.UseRateLimiter();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "HappyQOTD");

app.MapGet(
    "/api/quotes/today",
    async (
        IQuoteRepository quoteRepository,
        CancellationToken cancellationToken) =>
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var quote = await quoteRepository.GetQuoteOfTheDayAsync(
            today,
            cancellationToken);

        return quote is null
            ? Results.NotFound()
            : Results.Ok(quote);
    }).RequireRateLimiting(QuoteReadRateLimitPolicy);

app.MapPost(
        "/api/quotes",
        async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var errors = QuoteValidator.ValidateQuote(request);

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var created = await repository.InsertQuoteAsync(
                request,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        })
    .AddEndpointFilter<ApiKeyEndpointFilter>()
    .RequireRateLimiting(QuoteWriteRateLimitPolicy);

app.MapGet(
    "/api/quotes/random",
    async (IQuoteRepository quoteRepo,
CancellationToken cancellationToken) =>
{
    var quote = await quoteRepo.GetRandomQuoteAsync(cancellationToken);

    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
}).RequireRateLimiting(QuoteReadRateLimitPolicy);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Run();