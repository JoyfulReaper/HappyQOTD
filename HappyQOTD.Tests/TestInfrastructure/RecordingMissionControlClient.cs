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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventSignals =
        new();
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _canceledCount;

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
    public int CanceledCount => Volatile.Read(ref _canceledCount);

    public void Clear()
    {
        while (Calls.TryDequeue(out _))
        {
        }

        foreach (SemaphoreSlim signal in _eventSignals.Values)
        {
            while (signal.Wait(0))
            {
            }
        }
    }

    public void Release() => _release.TrySetResult();

    public async Task<MissionControlCall> WaitForEventAsync(
        string eventType,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        SemaphoreSlim signal = _eventSignals.GetOrAdd(
            eventType,
            _ => new SemaphoreSlim(0));

        while (true)
        {
            MissionControlCall? call = Calls.FirstOrDefault(
                call => call.EventType == eventType);

            if (call is not null)
            {
                return call;
            }

            await signal.WaitAsync(cancellation.Token);
        }
    }

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
        _eventSignals.GetOrAdd(
            eventType,
            _ => new SemaphoreSlim(0)).Release();

        if (_exception is not null)
        {
            throw _exception;
        }

        if (DelayUntilReleased)
        {
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledCount);
                throw;
            }
        }

        return _returnValue;
    }
}
