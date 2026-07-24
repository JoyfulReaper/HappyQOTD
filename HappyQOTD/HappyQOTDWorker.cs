/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using HappyQOTD.Events;
using HappyQOTD.Quotes;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace HappyQOTD;

public class HappyQOTDWorker(
    ILogger<HappyQOTDWorker> logger,
    IOptions<HappyQOTDOptions> options,
    IQuoteRepository quoteRepository,
    IMissionControlClient missionControlClient) : BackgroundService
{
    private static readonly TimeSpan TelemetryPublishTimeout =
        TimeSpan.FromSeconds(2);

    private TcpListener? _listener;
    private readonly ConcurrentDictionary<long, Task> _activeConnections = new();
    private volatile bool _stopRequested;
    private readonly SemaphoreSlim _connectionLimit = new(
        options.Value.MaxConcurrentConnections,
        options.Value.MaxConcurrentConnections);
    private long _nextConnectionId;

    public int BoundPort { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPAddress ipAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);
        _listener = new TcpListener(ipAddress, options.Value.Port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var occurredAt = DateTimeOffset.UtcNow;

        await PublishServiceStartedTelemetryAsync(
            $"{ipAddress}:{options.Value.Port}",
            occurredAt,
            stoppingToken);

        logger.LogInformation(
            "HappyQOTD Server Listening on {address}:{port}",
            ipAddress,
            options.Value.Port
        );

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (stoppingToken.IsCancellationRequested || _stopRequested)
                {
                    break;
                }
                try
                {
                    await _connectionLimit.WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    client.Dispose();
                    break;
                }

                long connectionId = Interlocked.Increment(ref _nextConnectionId);
                Task task = HandleClientAsync(connectionId, client, stoppingToken);
                _activeConnections[connectionId] = task;

                _ = task.ContinueWith(ct =>
                {
                    _activeConnections.TryRemove(connectionId, out _);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            }
        }
        finally
        {
            _listener.Stop();
            Task[] remaining = _activeConnections.Values.ToArray();
            if (remaining.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remaining);
                }
                catch
                {
                    // Normal Shutdown
                }
            }

            logger.LogInformation("HappyQOTD server stopped.");
        }
    }

    private async Task HandleClientAsync(
        long connectionId,
        TcpClient client,
        CancellationToken stoppingToken)
    {
        QotdSessionResult? telemetry = null;

        using (client)
        {
            try
            {
                client.NoDelay = true;

                await using NetworkStream stream =
                    client.GetStream();

                telemetry = await QotdConnectionHandler.ProcessAsync(
                    connectionId,
                    stream,
                    client.Client.RemoteEndPoint,
                    quoteRepository,
                    options.Value,
                    logger,
                    stoppingToken);
            }
            finally
            {
                _connectionLimit.Release();
            }
        }

        if (telemetry is not null)
        {
            await PublishQotdServedTelemetryAsync(
                telemetry,
                stoppingToken);
        }
    }

    private async Task PublishQotdServedTelemetryAsync(
        QotdSessionResult telemetry,
        CancellationToken stoppingToken)
    {
        using CancellationTokenSource timeoutTokenSource =
            new(TelemetryPublishTimeout);
        using CancellationTokenSource publishTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                timeoutTokenSource.Token);

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: QOTDServedEvent.EventName,
                payload: new QOTDServedEvent(
                    telemetry.Remote,
                    telemetry.DurationMilliseconds,
                    telemetry.Succeeded),
                payloadTypeInfo: QOTDJsonContext.Default.QOTDServedEvent,
                occurredAt: telemetry.OccurredAt,
                correlationId: telemetry.CorrelationId,
                cancellationToken: publishTokenSource.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept telemetry for QOTD client {Remote}.",
                    telemetry.Remote);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Telemetry publishing stopped for QOTD client {Remote}.",
                telemetry.Remote);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing telemetry for QOTD client {Remote}.",
                telemetry.Remote);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish telemetry for QOTD client {Remote}.",
                telemetry.Remote);
        }
    }

    private async Task PublishServiceStartedTelemetryAsync(
        string endpoint,
        DateTimeOffset occurredAt,
        CancellationToken stoppingToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: QOTDServiceStartedEvent.EventName,
                payload: new QOTDServiceStartedEvent(endpoint),
                payloadTypeInfo: QOTDJsonContext.Default.QOTDServiceStartedEvent,
                occurredAt: occurredAt,
                correlationId: null,
                cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}",
                    QOTDServiceStartedEvent.EventName);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Service-started telemetry publishing stopped during shutdown.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing Mission Control event for QOTD Service Started.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for QOTD Service Started");
        }
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyQOTD Server Stopping...");
        _stopRequested = true;
        _listener?.Stop();

        return base.StopAsync(cancellationToken);
    }
}
