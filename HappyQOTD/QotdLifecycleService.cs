/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Events;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;

namespace HappyQOTD;

public sealed class QotdLifecycleService(
    ILogger<QotdLifecycleService> logger,
    IMissionControlClient missionControlClient,
    IOptions<HappyQOTDOptions> options) : IHostedLifecycleService
{
    private static readonly TimeSpan TelemetryPublishTimeout = TimeSpan.FromSeconds(2); // TODO Make configurable.

    public Task StartingAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var listenAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);

        logger.LogInformation(
            "HappyQOTD TCP server started on {Address}:{Port}",
            listenAddress,
            options.Value.Port);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: QOTDServiceStartedEvent.EventName,
                    payload: new QOTDServiceStartedEvent(
                        $"{listenAddress}:{options.Value.Port}"),
                    payloadTypeInfo: QOTDJsonContext.Default.QOTDServiceStartedEvent,
                    occurredAt: DateTimeOffset.UtcNow,
                    correlationId: null,
                    cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}",
                    QOTDServiceStartedEvent.EventName);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "QOTD service-started telemetry publishing stopped during shutdown.");
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
                "Failed to publish Mission Control event for QOTD Service Started.");
        }
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyQOTD TCP server stopping...");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}