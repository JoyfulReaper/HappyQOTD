using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization.Metadata;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Tests.TestInfrastructure;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyQOTD.Tests;

public sealed class TcpQotdServerTests
{
    private static readonly TimeSpan HostTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ShortTimeout =
        TimeSpan.FromSeconds(2);

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
            ShortTimeout);
        var secondRead = await stream.ReadAsync(buffer).AsTask().WaitAsync(
            ShortTimeout);

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
    public async Task ConnectionSlotIsReleasedBeforeTelemetryCompletes()
    {
        var missionControl = new BlockingServedMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Limited"),
            missionControl,
            maxConcurrentConnections: 1);

        try
        {
            Assert.Equal("Limited\r\n", await ReadQuoteAsync(server.Port));
            await missionControl.WaitForStartedCountAsync(1, ShortTimeout);

            Task<string> secondRead = ReadQuoteAsync(server.Port);

            Assert.Equal(
                "Limited\r\n",
                await secondRead.WaitAsync(ShortTimeout));
            await missionControl.WaitForStartedCountAsync(2, ShortTimeout);
        }
        finally
        {
            missionControl.Release();
        }
    }

    [Fact]
    public async Task SharedHostWaitsForConnectionSlotWhileQuoteLookupRuns()
    {
        var repository = new BlockingQuoteRepository(
            new Quote(1, "Lookup limited"));
        await using var server = await TcpServerHarness.StartAsync(
            repository,
            maxConcurrentConnections: 1);

        try
        {
            Task<string> firstRead = ReadQuoteAsync(server.Port);
            await repository.WaitForStartedCountAsync(1, ShortTimeout);

            Task<string> secondRead = ReadQuoteAsync(server.Port);

            await Task.Delay(TimeSpan.FromMilliseconds(250));
            Assert.False(secondRead.IsCompleted);
            Assert.Equal(1, repository.StartedCount);

            repository.Release();

            string[] responses = await Task.WhenAll(
                firstRead,
                secondRead).WaitAsync(ShortTimeout);
            Assert.All(
                responses,
                response => Assert.Equal("Lookup limited\r\n", response));
        }
        finally
        {
            repository.Release();
        }
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

        await server.StopAsync(HostTimeout);

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
        await missionControl.WaitForEventAsync(
            QOTDServedEvent.EventName,
            ShortTimeout);

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
        await Task.Delay(TimeSpan.FromMilliseconds(250));

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

        MissionControlCall call = await missionControl.WaitForEventAsync(
            QOTDServedEvent.EventName,
            ShortTimeout);
        Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        var payload = Assert.IsType<QOTDServedEvent>(call.Payload);
        Assert.True(payload.Succeeded);
        Assert.True(payload.DurationMilliseconds >= 0);
        Assert.NotEqual("unknown", payload.Remote);
    }

    [Fact]
    public async Task MissionControlFalseReturnDoesNotPreventQuoteResponse()
    {
        var missionControl = new RecordingMissionControlClient(
            returnValue: false);
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "False return"),
            missionControl);
        missionControl.Clear();

        var response = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForEventAsync(
            QOTDServedEvent.EventName,
            ShortTimeout);

        Assert.Equal("False return\r\n", response);
    }

    [Fact]
    public async Task StartupTelemetryContainsExpectedValues()
    {
        var missionControl = new RecordingMissionControlClient();

        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Startup telemetry"),
            missionControl);

        MissionControlCall call = Assert.Single(
            missionControl.Calls,
            call => call.EventType == QOTDServiceStartedEvent.EventName);
        Assert.Null(call.CorrelationId);
        var payload = Assert.IsType<QOTDServiceStartedEvent>(call.Payload);
        Assert.Equal($"127.0.0.1:{server.Port}", payload.ListenAddress);
    }

    [Fact]
    public async Task StartupTelemetryTimeoutDoesNotPreventQuoteResponse()
    {
        var missionControl = new RecordingMissionControlClient(
            delayUntilReleased: true);
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Startup timeout"),
            missionControl);

        var response = await ReadQuoteAsync(server.Port, HostTimeout);

        Assert.Equal("Startup timeout\r\n", response);
        Assert.Equal(1, missionControl.CanceledCount);
        Assert.Single(
            missionControl.Calls,
            call => call.EventType == QOTDServiceStartedEvent.EventName);
    }

    [Fact]
    public async Task StartupTelemetryFalseReturnDoesNotPreventQuoteResponse()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Startup false"),
            new StartupOnlyMissionControlClient(returnValue: false));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Startup false\r\n", response);
    }

    [Fact]
    public async Task StartupTelemetryExceptionDoesNotPreventQuoteResponse()
    {
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Startup exception"),
            new StartupOnlyMissionControlClient(
                exception: new InvalidOperationException("boom")));

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("Startup exception\r\n", response);
    }

    [Fact]
    public async Task ClientReceivesEofBeforeTelemetryCompletes()
    {
        var missionControl = new BlockingServedMissionControlClient();
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "EOF"),
            missionControl);

        var response = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(1, ShortTimeout);

        Assert.Equal("EOF\r\n", response);
        Assert.Equal(0, missionControl.FinishedCount);

        missionControl.Release();
        await missionControl.WaitForFinishedCountAsync(1, ShortTimeout);
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
        await missionControl.WaitForStartedCountAsync(1, ShortTimeout);

        missionControl.Release();
        await missionControl.WaitForFinishedCountAsync(1, ShortTimeout);

        var secondResponse = await ReadQuoteAsync(server.Port);
        await missionControl.WaitForStartedCountAsync(2, ShortTimeout);

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
        await missionControl.WaitForStartedCountAsync(1, ShortTimeout);
        await missionControl.WaitForFinishedCountAsync(1, HostTimeout);

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
            ShortTimeout);
        client.Dispose();

        var response = await ReadQuoteAsync(server.Port);

        Assert.Equal("After disconnect\r\n", response);
    }

    private static Task<string> ReadQuoteAsync(int port) =>
        ReadQuoteAsync(port, ShortTimeout);

    private static async Task<string> ReadQuoteAsync(
        int port,
        TimeSpan timeout)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(
            ShortTimeout);

        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().WaitAsync(timeout);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(
            IPAddress.Loopback,
            0);

        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class TcpServerHarness : IAsyncDisposable
    {
        private readonly IHost _host;

        private TcpServerHarness(
            IHost host,
            int port)
        {
            _host = host;
            Port = port;
        }

        public int Port { get; }
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
            IQuoteRepository repository,
            IMissionControlClient? missionControl = null,
            int maxConcurrentConnections = 4,
            string[]? ignoredTelemetryAddresses = null)
        {
            int port = GetAvailablePort();
            var missionControlClient =
                missionControl ?? new RecordingMissionControlClient();
            var options = new HappyQOTDOptions
            {
                ListenAddress = "127.0.0.1",
                Port = port,
                EnableTcpServer = true,
                MaxConcurrentConnections =
                    maxConcurrentConnections,
                RequestTimeoutSeconds = 15,
                TelemetryIgnoredRemoteAddresses =
                    ignoredTelemetryAddresses ?? []
            };

            IHost host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                    logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IQuoteRepository>(
                        repository);

                    services.AddSingleton<IMissionControlClient>(
                        missionControlClient);

                    services.AddSingleton<IOptions<HappyQOTDOptions>>(
                        Options.Create(options));

                    services.AddTcpServer<
                        QOTDConnectionHandler,
                        HappyQOTDOptions>();

                    services.AddHostedService<QotdLifecycleService>();
                })
                .Build();

            try
            {
                using var startupTimeout =
                    new CancellationTokenSource(HostTimeout);

                await host.StartAsync(startupTimeout.Token);
                return new TcpServerHarness(host, port);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (Stopped)
            {
                return;
            }

            using var stopTimeout = new CancellationTokenSource(
                timeout ?? HostTimeout);

            await _host.StopAsync(stopTimeout.Token);
            Stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                _host.Dispose();
            }
        }
    }

    private sealed class BlockingQuoteRepository : IQuoteRepository
    {
        private readonly Quote _quote;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _startedSignal = new(0);
        private int _startedCount;

        public BlockingQuoteRepository(Quote quote)
        {
            _quote = quote;
        }

        public int StartedCount => Volatile.Read(ref _startedCount);

        public async Task<Quote?> GetQuoteOfTheDayAsync(
            DateOnly date,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _startedCount);
            _startedSignal.Release();
            await _release.Task.WaitAsync(cancellationToken);
            return _quote;
        }

        public Task<Quote?> GetRandomQuoteAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Quote?>(null);

        public Task<Quote> InsertQuoteAsync(
            CreateQuoteRequest quote,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Quote?> GetQuoteAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Quote?>(null);

        public Task<bool> DeleteQuoteAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> SetQuoteOfTheDayAsync(
            DateOnly date,
            long? quoteId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

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

    private sealed class StartupOnlyMissionControlClient : IMissionControlClient
    {
        private readonly bool _returnValue;
        private readonly Exception? _exception;

        public StartupOnlyMissionControlClient(
            bool returnValue = true,
            Exception? exception = null)
        {
            _returnValue = returnValue;
            _exception = exception;
        }

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            if (eventType == QOTDServiceStartedEvent.EventName)
            {
                if (_exception is not null)
                {
                    throw _exception;
                }

                return Task.FromResult(_returnValue);
            }

            return Task.FromResult(true);
        }
    }
}
