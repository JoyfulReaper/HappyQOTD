/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Events;
using HappyQOTD.Quotes;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyQOTD;

public sealed class QOTDConnectionHandler(
    ILogger<QOTDConnectionHandler> logger,
    IOptions<HappyQOTDOptions> options,
    IQuoteRepository quoteRepository,
    IMissionControlClient missionControlClient)
    : ITcpConnectionHandler
{
    private static readonly TimeSpan TelemetryPublishTimeout =
        TimeSpan.FromSeconds(2);

    public async ValueTask HandleAsync(
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        QotdSessionResult? result = await ProcessAsync(
            context.ConnectionId,
            context.Stream,
            context.RemoteEndPoint,
            quoteRepository,
            options.Value,
            logger,
            cancellationToken);

        if (result is null)
        {
            return;
        }

        long connectionId = context.ConnectionId;

        context.RegisterAfterClose(afterCloseToken =>
            PublishQotdServedTelemetryAsync(
                connectionId,
                result,
                afterCloseToken));
    }

    internal static async Task<QotdSessionResult?> ProcessAsync(
        long connectionId,
        Stream stream,
        EndPoint? remote,
        IQuoteRepository quoteRepository,
        HappyQOTDOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string correlationId = Guid.NewGuid().ToString("N");

        bool responseCompleted = false;
        string remoteString = remote?.ToString() ?? "unknown";

        bool isIgnoredTelemetrySource =
            IsIgnoredTelemetrySource(
                remote,
                options.TelemetryIgnoredRemoteAddresses);

        try
        {
            DateOnly today =
                DateOnly.FromDateTime(DateTime.UtcNow);

            Quote? quote =
                await quoteRepository.GetQuoteOfTheDayAsync(
                    today,
                    cancellationToken);

            string response = quote is null
                ? "No quote is available today.\r\n"
                : FormatQuote(quote);

            byte[] responseBytes =
                Encoding.UTF8.GetBytes(response);

            await stream.WriteAsync(
                responseBytes,
                cancellationToken);

            await stream.FlushAsync(cancellationToken);

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
        finally
        {
            stopwatch.Stop();
        }

        if (isIgnoredTelemetrySource)
        {
            return null;
        }

        return new QotdSessionResult(
            Remote: remoteString,
            DurationMilliseconds: stopwatch.ElapsedMilliseconds,
            Succeeded: responseCompleted,
            OccurredAt: occurredAt,
            CorrelationId: correlationId);
    }

    private async ValueTask PublishQotdServedTelemetryAsync(
        long connectionId,
        QotdSessionResult result,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: QOTDServedEvent.EventName,
                    payload: new QOTDServedEvent(
                        result.Remote,
                        result.DurationMilliseconds,
                        result.Succeeded),
                    payloadTypeInfo:
                        QOTDJsonContext.Default.QOTDServedEvent,
                    occurredAt: result.OccurredAt,
                    correlationId: result.CorrelationId,
                    cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept telemetry for QOTD connection {ConnectionId} from {Remote}.",
                    connectionId,
                    result.Remote);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Telemetry publishing stopped for QOTD connection {ConnectionId} from {Remote}.",
                connectionId,
                result.Remote);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing telemetry for QOTD connection {ConnectionId} from {Remote}.",
                connectionId,
                result.Remote);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish telemetry for QOTD connection {ConnectionId} from {Remote}.",
                connectionId,
                result.Remote);
        }
    }

    internal static bool IsIgnoredTelemetrySource(
        EndPoint? remote,
        IEnumerable<string> ignoredRemoteAddresses)
    {
        IPAddress? remoteAddress =
            (remote as IPEndPoint)?
                .Address
                .MapToIPv4();

        if (remoteAddress is null)
        {
            return false;
        }

        return ignoredRemoteAddresses.Any(
            configuredAddress =>
                IPAddress.TryParse(
                    configuredAddress,
                    out IPAddress? ignoredAddress) &&
                remoteAddress.Equals(
                    ignoredAddress.MapToIPv4()));
    }

    internal static string FormatQuote(Quote quote)
    {
        string? attribution = quote.Author;

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
}

internal sealed record QotdSessionResult(
    string Remote,
    long DurationMilliseconds,
    bool Succeeded,
    DateTimeOffset OccurredAt,
    string CorrelationId);
