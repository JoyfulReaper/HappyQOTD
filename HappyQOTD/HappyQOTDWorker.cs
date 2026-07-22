using HappyQOTD.Events;
using HappyQOTD.Quotes;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: QOTDServiceStartedEvent.EventName,
                payload: new QOTDServiceStartedEvent(
                    $"{ipAddress}:{options.Value.Port}"),
                payloadTypeInfo: QOTDJsonContext.Default.QOTDServiceStartedEvent,
                occurredAt: occurredAt,
                correlationId: null,
                cancellationToken: stoppingToken);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}",
                    QOTDServiceStartedEvent.EventName);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for QOTD Service Started");
        }

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

    private async Task HandleClientAsync(long connectionId, TcpClient client, CancellationToken stoppingToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        bool responseCompleted = false;
        EndPoint? remote = null;
        bool isIgnoredTelemetrySource = false;
        QotdServedTelemetryResult? telemetry = null;

        try
        {
            using (client)
            {
                client.NoDelay = true;
                remote = client.Client.RemoteEndPoint;

                isIgnoredTelemetrySource = IsIgnoredTelemetrySource(remote);

                try
                {
                    DateOnly today =
                        DateOnly.FromDateTime(
                            DateTime.UtcNow);

                    Quote? quote =
                        await quoteRepository.GetQuoteOfTheDayAsync(
                            today,
                            stoppingToken);

                    string response = quote is null
                        ? "No quote is available today.\r\n"
                        : FormatQuote(quote);

                    byte[] responseBytes =
                        Encoding.UTF8.GetBytes(response);

                    await using NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(responseBytes, stoppingToken);
                    await stream.FlushAsync(stoppingToken);
                    responseCompleted = true;
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning(
                        "Connection {ConnectionId} from {Remote} timed out.",
                        connectionId,
                        remote);
                }
                catch (InvalidDataException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Rejected malformed request on connection {ConnectionId} from {Remote}.",
                        connectionId,
                        remote);
                }
                catch (IOException exception)
                {
                    logger.LogDebug(
                        exception,
                        "Connection {ConnectionId} from {Remote} ended early.",
                        connectionId,
                        remote);
                }
                catch (SocketException exception)
                {
                    logger.LogDebug(
                        exception,
                        "Socket error on connection {ConnectionId} from {Remote}.",
                        connectionId,
                        remote);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Unhandled error on connection {ConnectionId} from {Remote}.",
                        connectionId,
                        remote);
                }

                stopwatch.Stop();
            }

            if (isIgnoredTelemetrySource)
            {
                return;
            }

            telemetry = new QotdServedTelemetryResult(
                remote?.ToString() ?? "unknown",
                stopwatch.ElapsedMilliseconds,
                responseCompleted,
                occurredAt,
                correlationId);
        }
        finally
        {
            _connectionLimit.Release();
        }

        if (telemetry is not null)
        {
            await PublishQotdServedTelemetryAsync(
                telemetry,
                stoppingToken);
        }
    }

    private async Task PublishQotdServedTelemetryAsync(
        QotdServedTelemetryResult telemetry,
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

    private bool IsIgnoredTelemetrySource(
        EndPoint? remote)
    {
        IPAddress? remoteAddress =
            (remote as IPEndPoint)?
                .Address
                .MapToIPv4();

        if (remoteAddress is null)
        {
            return false;
        }

        return options.Value
            .TelemetryIgnoredRemoteAddresses
            .Any(configuredAddress =>
                IPAddress.TryParse(
                    configuredAddress,
                    out IPAddress? ignoredAddress) &&
                remoteAddress.Equals(
                    ignoredAddress.MapToIPv4()));
    }

    private static string FormatQuote(Quote quote)
    {
        var attribution = quote.Author;

        if (!string.IsNullOrWhiteSpace(quote.Source))
        {
            attribution = string.IsNullOrWhiteSpace(attribution)
                ? quote.Source
                : $"{attribution}, {quote.Source}";
        }

        return string.IsNullOrWhiteSpace(attribution)
            ? $"{quote.Text}\r\n"
            : $"{quote.Text}\r\n-- {attribution}\r\n";
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyQOTD Server Stopping...");
        _stopRequested = true;
        _listener?.Stop();

        return base.StopAsync(cancellationToken);
    }

    private sealed record QotdServedTelemetryResult(
        string Remote,
        long DurationMilliseconds,
        bool Succeeded,
        DateTimeOffset OccurredAt,
        string CorrelationId);
}
