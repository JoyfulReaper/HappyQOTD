using HappyQOTD.Events;
using HappyQOTD.Quotes;
using HappyQOTD.Tests.TestInfrastructure;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyQOTD.Tests;

public sealed class UdpQotdServerTests
{
    private static readonly TimeSpan HostTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ShortTimeout =
        TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DatagramReturnsCurrentQuote()
    {
        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "UDP quote", "Author", "Source"));

        string response = await ReadQuoteAsync(server.Port);

        Assert.Equal("UDP quote\r\n-- Author, Source\r\n", response);
    }

    [Fact]
    public async Task DatagramIgnoresRequestPayload()
    {
        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "Payload ignored"));

        string response = await ReadQuoteAsync(
            server.Port,
            "garbage input that should not matter");

        Assert.Equal("Payload ignored\r\n", response);
    }

    [Fact]
    public async Task MissingQuoteReturnsFallbackMessage()
    {
        await using var server = await UdpServerHarness.StartAsync(
            quote: null);

        string response = await ReadQuoteAsync(server.Port);

        Assert.Equal("No quote is available today.\r\n", response);
    }

    [Fact]
    public async Task LongQuoteIsTruncatedWhenConfigured()
    {
        string longText = new('x', 600);

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, longText),
            truncateQuoteResponses: true,
            maximumQuoteResponseCharacters: 512);

        string response = await ReadQuoteAsync(server.Port);

        Assert.Equal(512, response.Length);
        Assert.EndsWith("\r\n", response);
    }

    [Fact]
    public async Task LongQuoteIsNotTruncatedWhenConfigured()
    {
        string longText = new('x', 600);

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, longText),
            truncateQuoteResponses: false,
            maximumQuoteResponseCharacters: 512);

        string response = await ReadQuoteAsync(server.Port);

        Assert.Equal($"{longText}\r\n", response);
        Assert.True(response.Length > 512);
    }

    [Fact]
    public async Task ServedTelemetryUsesUdpProtocol()
    {
        var missionControl = new RecordingMissionControlClient();

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "Telemetry"),
            missionControl);

        missionControl.Clear();

        _ = await ReadQuoteAsync(server.Port);

        MissionControlCall call =
            await missionControl.WaitForEventAsync(
                QOTDServedEvent.EventName,
                ShortTimeout);

        Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));

        var payload = Assert.IsType<QOTDServedEvent>(call.Payload);
        Assert.True(payload.Succeeded);
        Assert.True(payload.DurationMilliseconds >= 0);
        Assert.NotEqual("unknown", payload.Remote);
        Assert.Equal(QOTDServedEvent.UdpProtocol, payload.Protocol);
    }

    private static async Task WaitUntilUdpServerRespondsAsync(
    int port,
    CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                _ = await ReadQuoteAsync(
                    port,
                    "ready",
                    TimeSpan.FromMilliseconds(250));

                return;
            }
            catch (SocketException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (TimeoutException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                cancellationToken);
        }
    }

    private static Task<string> ReadQuoteAsync(
        int port,
        string payload = "hello") =>
        ReadQuoteAsync(
            port,
            payload,
            ShortTimeout);

    private static async Task<string> ReadQuoteAsync(
        int port,
        string payload,
        TimeSpan timeout)
    {
        using var client = new UdpClient();

        byte[] requestBytes =
            Encoding.ASCII.GetBytes(payload);

        await client.SendAsync(
            requestBytes,
            requestBytes.Length,
            new IPEndPoint(IPAddress.Loopback, port));

        Task<UdpReceiveResult> receiveTask =
            client.ReceiveAsync();

        UdpReceiveResult result =
            await receiveTask.WaitAsync(timeout);

        return Encoding.UTF8.GetString(result.Buffer);
    }

    private static int GetAvailablePort()
    {
        using var socket =
            new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);

        socket.Bind(
            new IPEndPoint(
                IPAddress.Loopback,
                0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private sealed class UdpServerHarness : IAsyncDisposable
    {
        private readonly IHost _host;

        private UdpServerHarness(
            IHost host,
            int port)
        {
            _host = host;
            Port = port;
        }

        public int Port { get; }
        public bool Stopped { get; private set; }

        public static async Task<UdpServerHarness> StartAsync(
            Quote? quote,
            IMissionControlClient? missionControl = null,
            bool truncateQuoteResponses = true,
            int maximumQuoteResponseCharacters = 512)
        {
            int port = GetAvailablePort();

            var missionControlClient =
                missionControl ?? new RecordingMissionControlClient();

            var options = new HappyQOTDOptions
            {
                ListenAddress = "127.0.0.1",
                Port = port,
                EnableTcpServer = false,
                EnableUdpServer = true,
                TruncateQuoteResponses = truncateQuoteResponses,
                MaximumQuoteResponseCharacters =
                    maximumQuoteResponseCharacters,
                TelemetryIgnoredRemoteAddresses = []
            };

            IHost host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                    logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IQuoteRepository>(
                        new FakeQuoteRepository(quote));

                    services.AddSingleton<IMissionControlClient>(
                        missionControlClient);

                    services.AddSingleton<IOptions<HappyQOTDOptions>>(
                        Options.Create(options));

                    services.AddHostedService<QotdUdpServer>();
                })
                .Build();

            try
            {
                using var startupTimeout =
                    new CancellationTokenSource(HostTimeout);

                await host.StartAsync(startupTimeout.Token);

                await WaitUntilUdpServerRespondsAsync(
                    port,
                    startupTimeout.Token);

                return new UdpServerHarness(
                    host,
                    port);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        public async Task StopAsync(
            TimeSpan? timeout = null)
        {
            if (Stopped)
            {
                return;
            }

            using var stopTimeout =
                new CancellationTokenSource(
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
}