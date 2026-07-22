using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization.Metadata;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Tests.TestInfrastructure;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HappyQOTD.Tests;

public sealed class TcpQotdServerTests
{
    [Fact]
    public async Task ConnectingReturnsCurrentQuoteWithAuthorAndSource()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Quote text", "Author", "Source"));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Quote text\r\n-- Author, Source\r\n", response);
    }

    [Fact]
    public async Task QuoteWithOnlySourceUsesSourceAttribution()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Quote text", null, "Source"));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Quote text\r\n-- Source\r\n", response);
    }

    [Fact]
    public async Task QuoteWithoutAttributionHasNoAttributionLine()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Quote text"));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Quote text\r\n", response);
    }

    [Fact]
    public async Task MissingQuoteReturnsFallbackMessage()
    {
        await using var server = await TcpServerHarness.StartAsync(quote: null);

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("No quote is available today.\r\n", response);
    }

    [Fact]
    public async Task ServerClosesConnectionAfterSendingResponse()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Quote text"));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        using var stream = client.GetStream();
        var buffer = new byte[128];

        var firstRead = await stream.ReadAsync(buffer).AsTask().WaitAsync(
            TimeSpan.FromSeconds(2));
        var secondRead = await stream.ReadAsync(buffer).AsTask().WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.True(firstRead > 0);
        Assert.Equal(0, secondRead);
    }

    [Fact]
    public async Task MultipleConcurrentClientsReceiveResponses()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Concurrent"),
            maxConcurrentConnections: 8);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => ReadQuoteAsync(server.Port)));

        Assert.All(responses, response => Assert.Equal("Concurrent\r\n", response));
    }

    [Fact]
    public async Task MaximumConcurrentConnectionLimitIsRespected()
    {
        var missionControl = new BlockingServedMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Limited"),
            missionControl,
            maxConcurrentConnections: 1);

        var firstRead = ReadQuoteAsync(server.Port);
        Assert.Equal("Limited\r\n", await firstRead);
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        var secondRead = ReadQuoteAsync(server.Port);

        missionControl.Release();
        Assert.Equal("Limited\r\n", await secondRead.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ServerStopsCleanly()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Quote text"));

        await server.StopAsync();

        Assert.True(server.Stopped);
    }

    [Fact]
    public async Task ShutdownWhileClientConnectionIsActiveDoesNotHang()
    {
        var repository = new FakeQuoteRepository(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        });
        await using var server = await TcpServerHarness.StartAsync(repository);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        await server.StopAsync(TimeSpan.FromSeconds(2));

        Assert.True(server.Stopped);
    }

    [Fact]
    public async Task MissionControlFailureDoesNotPreventQuoteResponse()
    {
        var missionControl = new RecordingMissionControlClient(
            exception: new InvalidOperationException("boom"));
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Still served"),
            missionControl);

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Still served\r\n", response);
    }

    [Fact]
    public async Task IgnoredTelemetryAddressesDoNotPublishServedEvents()
    {
        var missionControl = new RecordingMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Ignored"),
            missionControl,
            ignoredTelemetryAddresses: ["127.0.0.1"]);
        missionControl.Clear();

        _ = await ReadQuoteAsync(server.Port);

        Assert.DoesNotContain(
            missionControl.Calls,
            call => call.EventType == QOTDServedEvent.EventName);
    }

    [Fact]
    public async Task ServedTelemetryContainsExpectedValues()
    {
        var missionControl = new RecordingMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Telemetry"),
            missionControl);
        missionControl.Clear();

        _ = await ReadQuoteAsync(server.Port);

        var call = Assert.Single(
            missionControl.Calls,
            call => call.EventType == QOTDServedEvent.EventName);
        Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        var payload = Assert.IsType<QOTDServedEvent>(call.Payload);
        Assert.True(payload.Succeeded);
        Assert.True(payload.DurationMilliseconds >= 0);
        Assert.NotEqual("unknown", payload.Remote);
    }

    [Fact]
    public async Task MissionControlFalseReturnDoesNotPreventQuoteResponse()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "False return"),
            new RecordingMissionControlClient(returnValue: false));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("False return\r\n", response);
    }

    [Fact]
    public async Task ClientReceivesEofBeforeTelemetryCompletes()
    {
        var missionControl = new BlockingServedMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "EOF"),
            missionControl);

        var response = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        Assert.Equal("EOF\r\n", response);
        Assert.Equal(0, missionControl.FinishedCount);

        missionControl.Release();
        await missionControl.WaitForFinishedCountAsync(1, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TelemetryExceptionDoesNotBreakLaterTcpRequests()
    {
        var missionControl = new BlockingServedMissionControlClient(
            throwAfterRelease: true);
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Still alive"),
            missionControl);

        var firstResponse = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        missionControl.Release();
        await missionControl.WaitForFinishedCountAsync(1, TimeSpan.FromSeconds(2));

        var secondResponse = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(2, TimeSpan.FromSeconds(2));

        Assert.Equal("Still alive\r\n", firstResponse);
        Assert.Equal("Still alive\r\n", secondResponse);
    }

    [Fact]
    public async Task TelemetryTimeoutDoesNotHangHandler()
    {
        var missionControl = new BlockingServedMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Timeout"),
            missionControl,
            maxConcurrentConnections: 1);

        var response = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));
        await missionControl.WaitForFinishedCountAsync(1, TimeSpan.FromSeconds(5));

        Assert.Equal("Timeout\r\n", response);
        Assert.Equal(1, missionControl.CanceledCount);
    }

    [Fact]
    public async Task ConnectionSlotIsReleasedAfterClientDisconnect()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "After disconnect"),
            maxConcurrentConnections: 1);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port).WaitAsync(
            TimeSpan.FromSeconds(2));
        client.Dispose();

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("After disconnect\r\n", response);
    }

    private static async Task<string> ReadQuoteAsync(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(
            TimeSpan.FromSeconds(2));

        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class TcpServerHarness : IAsyncDisposable
    {
        private readonly HappyQOTDWorker _worker;

        private TcpServerHarness(HappyQOTDWorker worker)
        {
            _worker = worker;
        }

        public int Port => _worker.BoundPort;
        public bool Stopped { get; private set; }

        public static Task<TcpServerHarness> StartAsync(
            Quote? quote,
            IMissionControlClient? missionControl = null,
            int maxConcurrentConnections = 4,
            string[]? ignoredTelemetryAddresses = null) =>
            StartAsync(
                new FakeQuoteRepository(quote),
                missionControl,
                maxConcurrentConnections,
                ignoredTelemetryAddresses);

        public static async Task<TcpServerHarness> StartAsync(
            FakeQuoteRepository repository,
            IMissionControlClient? missionControl = null,
            int maxConcurrentConnections = 4,
            string[]? ignoredTelemetryAddresses = null)
        {
            var worker = new HappyQOTDWorker(
                NullLogger<HappyQOTDWorker>.Instance,
                Options.Create(new HappyQOTDOptions
                {
                    ListenAddress = "127.0.0.1",
                    Port = 0,
                    MaxConcurrentConnections = maxConcurrentConnections,
                    TelemetryIgnoredRemoteAddresses = ignoredTelemetryAddresses ?? []
                }),
                repository,
                missionControl ?? new RecordingMissionControlClient());

            var harness = new TcpServerHarness(worker);
            await worker.StartAsync(CancellationToken.None);
            await harness.WaitForPortAsync();
            return harness;
        }

        private async Task WaitForPortAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Port == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, timeout.Token);
            }
        }

        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (Stopped)
            {
                return;
            }

            using var stopTimeout = new CancellationTokenSource(
                timeout ?? TimeSpan.FromSeconds(5));
            await _worker.StopAsync(stopTimeout.Token);
            Stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }

    private sealed class BlockingServedMissionControlClient : IMissionControlClient
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _startedSignal = new(0);
        private readonly SemaphoreSlim _finishedSignal = new(0);
        private readonly bool _throwAfterRelease;
        private int _startedCount;
        private int _finishedCount;
        private int _canceledCount;

        public BlockingServedMissionControlClient(
            bool throwAfterRelease = false)
        {
            _throwAfterRelease = throwAfterRelease;
        }

        public int StartedCount => Volatile.Read(ref _startedCount);
        public int FinishedCount => Volatile.Read(ref _finishedCount);
        public int CanceledCount => Volatile.Read(ref _canceledCount);

        public async Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            if (eventType != QOTDServedEvent.EventName)
            {
                return true;
            }

            Interlocked.Increment(ref _startedCount);
            _startedSignal.Release();

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledCount);
                throw;
            }
            finally
            {
                Interlocked.Increment(ref _finishedCount);
                _finishedSignal.Release();
            }

            if (_throwAfterRelease)
            {
                throw new InvalidOperationException("Telemetry failure");
            }

            return true;
        }

        public void Release() =>
            _release.TrySetResult();

        public async Task WaitForStartedCountAsync(
            int expectedCount,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (StartedCount < expectedCount)
            {
                await _startedSignal.WaitAsync(cancellation.Token);
            }
        }

        public async Task WaitForFinishedCountAsync(
            int expectedCount,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (FinishedCount < expectedCount)
            {
                await _finishedSignal.WaitAsync(cancellation.Token);
            }
        }
    }
}
