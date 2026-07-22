using System.Net;
using System.Net.Sockets;
using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Tests.TestInfrastructure;
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
        var missionControl = new RecordingMissionControlClient(delayUntilReleased: true);
        await using var server = await TcpServerHarness.StartAsync(
            new Quote(1, "Limited"),
            missionControl,
            maxConcurrentConnections: 1);
        missionControl.Clear();

        var firstRead = ReadQuoteAsync(server.Port);
        await missionControl.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        var secondRead = ReadQuoteAsync(server.Port);
        await Assert.ThrowsAsync<TimeoutException>(
            () => secondRead.WaitAsync(TimeSpan.FromMilliseconds(250)));

        missionControl.Release();
        Assert.Equal("Limited\r\n", await firstRead);
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
            RecordingMissionControlClient? missionControl = null,
            int maxConcurrentConnections = 4,
            string[]? ignoredTelemetryAddresses = null) =>
            StartAsync(
                new FakeQuoteRepository(quote),
                missionControl,
                maxConcurrentConnections,
                ignoredTelemetryAddresses);

        public static async Task<TcpServerHarness> StartAsync(
            FakeQuoteRepository repository,
            RecordingMissionControlClient? missionControl = null,
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
}
