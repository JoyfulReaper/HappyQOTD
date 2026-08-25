/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Events;
using HappyQOTD.Quotes;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyQOTD;

public sealed class QotdUdpServer(
    ILogger<QotdUdpServer> logger,
    IOptions<HappyQOTDOptions> options,
    IQuoteRepository quoteRepository,
    IMissionControlClient missionControlClient)
    : BackgroundService
{
    private static readonly TimeSpan TelemetryPublishTimeout = TimeSpan.FromSeconds(2);
    private Socket? _socket;

    public override async Task StartAsync(
    CancellationToken cancellationToken)
    {
        HappyQOTDOptions currentOptions = options.Value;

        _socket = CreateSocket(currentOptions);

        logger.LogInformation(
            "HappyQOTD UDP socket bound to {Endpoint} (dual mode: {DualMode}).",
            _socket.LocalEndPoint,
            currentOptions.DualMode);

        try
        {
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            _socket.Dispose();
            _socket = null;
            throw;
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        HappyQOTDOptions currentOptions = options.Value;

        Socket socket = _socket
            ?? throw new InvalidOperationException("UDP QOTD socket was not initialized.");

        logger.LogInformation(
            "HappyQOTD UDP server listening on {ListenAddress}:{Port} (dual mode: {DualMode}).",
            currentOptions.ListenAddress,
            currentOptions.Port,
            currentOptions.DualMode);

        byte[] buffer = new byte[1024];

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EndPoint remoteEndPoint =
                    socket.AddressFamily == AddressFamily.InterNetworkV6
                        ? new IPEndPoint(IPAddress.IPv6Any, 0)
                        : new IPEndPoint(IPAddress.Any, 0);

                SocketReceiveFromResult received;

                try
                {
                    received =
                        await socket.ReceiveFromAsync(
                            buffer,
                            SocketFlags.None,
                            remoteEndPoint,
                            stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    logger.LogWarning(
                        exception,
                        "UDP receive failed.");

                    continue;
                }

                await ServeDatagramAsync(
                    socket,
                    received.RemoteEndPoint,
                    stoppingToken);
            }
        }
        finally
        {
            socket.Dispose();
            _socket = null;

            logger.LogInformation(
                "HappyQOTD UDP server stopped.");
        }
    }

    private async Task ServeDatagramAsync(
        Socket socket,
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        string correlationId = Guid.NewGuid().ToString("N");
        string remote = remoteEndPoint.ToString() ?? "unknown";
        bool succeeded = false;

        try
        {
            string response = await GetResponseAsync(cancellationToken);
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);

            await socket.SendToAsync(
                responseBytes,
                SocketFlags.None,
                remoteEndPoint,
                cancellationToken);

            succeeded = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException exception)
        {
            logger.LogDebug(
                exception,
                "UDP send failed for {Remote}.",
                remote);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unhandled UDP QOTD error for {Remote}.",
                remote);
        }
        finally
        {
            stopwatch.Stop();
        }

        if (QOTDConnectionHandler.IsIgnoredTelemetrySource(
                remoteEndPoint,
                options.Value.TelemetryIgnoredRemoteAddresses))
        {
            return;
        }

        _ = PublishTelemetryAsync(
            remote,
            stopwatch.ElapsedMilliseconds,
            succeeded,
            occurredAt,
            correlationId,
            cancellationToken);
    }

    private async Task<string> GetResponseAsync(
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        Quote? quote = await quoteRepository.GetQuoteOfTheDayAsync(today, cancellationToken);

        string response = quote is null
            ? "No quote is available today.\r\n"
            : QotdResponseFormatter.FormatQuote(quote);

        return QotdResponseFormatter.ApplyLengthPolicy(
            response,
            options.Value);
    }

    private async Task PublishTelemetryAsync(
        string remote,
        long durationMilliseconds,
        bool succeeded,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: QOTDServedEvent.EventName,
                    payload: new QOTDServedEvent(
                        remote,
                        durationMilliseconds,
                        succeeded,
                        QOTDServedEvent.UdpProtocol),
                    payloadTypeInfo:
                        QOTDJsonContext.Default.QOTDServedEvent,
                    occurredAt: occurredAt,
                    correlationId: correlationId,
                    cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept UDP QOTD telemetry for {Remote}.",
                    remote);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "UDP QOTD telemetry publishing stopped for {Remote}.",
                remote);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing UDP QOTD telemetry for {Remote}.",
                remote);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish UDP QOTD telemetry for {Remote}.",
                remote);
        }
    }

    private static Socket CreateSocket(
        HappyQOTDOptions options)
    {
        IPAddress listenAddress =
            ParseListenAddress(options.ListenAddress);

        if (options.DualMode &&
            !listenAddress.Equals(IPAddress.IPv6Any))
        {
            throw new InvalidOperationException(
                "UDP dual mode requires ListenAddress to be the IPv6 wildcard address '::'.");
        }

        var socket =
            new Socket(
                listenAddress.AddressFamily,
                SocketType.Dgram,
                ProtocolType.Udp);

        socket.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true);

        if (listenAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            socket.DualMode = options.DualMode;
        }

        socket.Bind(
            new IPEndPoint(
                listenAddress,
                options.Port));

        return socket;
    }

    private static IPAddress ParseListenAddress(
        string listenAddress)
    {
        if (string.IsNullOrWhiteSpace(listenAddress) ||
            listenAddress is "*" or "+" or "any")
        {
            return IPAddress.IPv6Any;
        }

        if (string.Equals(
                listenAddress,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.IPv6Loopback;
        }

        return IPAddress.Parse(listenAddress);
    }
}
