using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using JoyfulReaperLib.MissionControl;

namespace HappyQOTD.Tests.TestInfrastructure;

internal sealed record MissionControlCall(
    string EventType,
    object? Payload,
    DateTimeOffset OccurredAt,
    string? CorrelationId);

internal sealed class RecordingMissionControlClient : IMissionControlClient
{
    private readonly bool _returnValue;
    private readonly Exception? _exception;
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RecordingMissionControlClient(
        bool returnValue = true,
        Exception? exception = null,
        bool delayUntilReleased = false)
    {
        _returnValue = returnValue;
        _exception = exception;
        DelayUntilReleased = delayUntilReleased;
    }

    public bool DelayUntilReleased { get; }
    public ConcurrentQueue<MissionControlCall> Calls { get; } = new();
    public Task Entered => _entered.Task;

    public void Clear()
    {
        while (Calls.TryDequeue(out _))
        {
        }
    }

    public void Release() => _release.TrySetResult();

    public async Task<bool> TryPublishAsync<TPayload>(
        string eventType,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadTypeInfo,
        DateTimeOffset occurredAt,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue(new MissionControlCall(
            eventType,
            payload,
            occurredAt,
            correlationId));

        _entered.TrySetResult();

        if (_exception is not null)
        {
            throw _exception;
        }

        if (DelayUntilReleased)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        return _returnValue;
    }
}
