/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Data;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;
using System.Threading.RateLimiting;

namespace HappyQOTD;

public static class HappyQotdApplicationExtensions
{
    private const string QuoteWriteRateLimitPolicy = "quote-write";
    private const string QuoteReadRateLimitPolicy = "quote-read";
    private const string BrowserClientCorsPolicy = "browser-client";

    public static IServiceCollection AddHappyQotdServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, QOTDJsonContext.Default);
        });

        services.AddCors(options =>
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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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
                    var remoteAddress = httpContext.Connection.RemoteIpAddress;

                    if (remoteAddress is not null &&
                        IPAddress.IsLoopback(remoteAddress))
                    {
                        return RateLimitPartition.GetNoLimiter(
                            "loopback");
                    }

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: remoteAddress?.ToString() ?? "unknown",
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

        var qotdOptions = configuration
            .GetSection(HappyQOTDOptions.SectionName)
            .Get<HappyQOTDOptions>() ?? new HappyQOTDOptions();

        services.Configure<HappyQOTDOptions>(
            configuration.GetSection(HappyQOTDOptions.SectionName));

        var quoteConnectionString = string.IsNullOrWhiteSpace(qotdOptions.QuoteConnectionString)
            ? QuoteDatabase.Initialize()
            : qotdOptions.QuoteConnectionString;

        services.AddSingleton<IQuoteRepository>(
            _ => new SqliteRepository(quoteConnectionString));

        services.AddHealthChecks()
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

        services.Configure<QotdSecurityOptions>(configuration.GetSection(QotdSecurityOptions.SectionName));
        services.AddMissionControlClient(configuration.GetSection(MissionControlClientOptions.SectionName));

        services.AddScoped<ApiKeyEndpointFilter>();
        if (qotdOptions.EnableTcpServer)
        {
            services.AddTcpServer<QOTDConnectionHandler, HappyQOTDOptions>();
            services.AddHostedService<QotdLifecycleService>();
        }
        services.AddProblemDetails();

        return services;
    }

    public static WebApplication UseHappyQotdMiddleware(this WebApplication app)
    {
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        forwardedOptions.KnownIPNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        forwardedOptions.KnownProxies.Add(IPAddress.Loopback);
        forwardedOptions.KnownProxies.Add(IPAddress.IPv6Loopback);

        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("CF-Visitor", out var cfVisitor) &&
                cfVisitor.ToString().Contains("\"scheme\":\"https\""))
            {
                context.Request.Headers["X-Forwarded-Proto"] = "https";
            }

            return next();
        });

        app.UseForwardedHeaders(forwardedOptions);
        app.UseCors(BrowserClientCorsPolicy);
        app.UseRateLimiter();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}
