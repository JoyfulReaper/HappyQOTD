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
using System.Reflection;
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
    public async Task DualModeIpv6WildcardAcceptsIpv4Loopback()
    {
        if (!CanBindIpv6Loopback())
        {
            return;
        }

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "Dual mode IPv4"),
            listenAddress: "::",
            dualMode: true);

        string response = await ReadQuoteAsync(
            server.Port,
            IPAddress.Loopback);

        Assert.Equal("Dual mode IPv4\r\n", response);
    }

    [Fact]
    public async Task DualModeIpv6WildcardAcceptsIpv6Loopback()
    {
        if (!CanBindIpv6Loopback())
        {
            return;
        }

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "Dual mode IPv6"),
            listenAddress: "::",
            dualMode: true);

        string response = await ReadQuoteAsync(
            server.Port,
            IPAddress.IPv6Loopback);

        Assert.Equal("Dual mode IPv6\r\n", response);
    }

    [Fact]
    public void DualModeRequiresIpv6WildcardListenAddress()
    {
        MethodInfo createSocket =
            typeof(QotdUdpServer).GetMethod(
                "CreateSocket",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        var options = new HappyQOTDOptions
        {
            ListenAddress = "127.0.0.1",
            DualMode = true
        };

        TargetInvocationException invocationException =
            Assert.Throws<TargetInvocationException>(() =>
                createSocket.Invoke(
                    null,
                    [options]));

        InvalidOperationException exception =
            Assert.IsType<InvalidOperationException>(
                invocationException.InnerException);

        Assert.Equal(
            "UDP dual mode requires ListenAddress to be the IPv6 wildcard address '::'.",
            exception.Message);
    }

    [Fact]
    public async Task KeepsServingWhileTelemetryIsBlocked()
    {
        var missionControl = new RecordingMissionControlClient(
            delayUntilReleased: true);

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "Still serving"),
            missionControl);

        string firstResponse =
            await ReadQuoteAsync(server.Port);

        await missionControl.Entered.WaitAsync(ShortTimeout);

        string secondResponse =
            await ReadQuoteAsync(
                server.Port,
                IPAddress.Loopback,
                "second",
                TimeSpan.FromMilliseconds(500));

        Assert.Equal(
            "Still serving\r\n",
            firstResponse);

        Assert.Equal(
            "Still serving\r\n",
            secondResponse);

        Assert.Equal(
            2,
            missionControl.Calls.Count(call =>
                call.EventType == QOTDServedEvent.EventName));

        missionControl.Release();
    }

    [Fact]
    public async Task Ipv6WildcardWithoutDualModeDoesNotAcceptIpv4Loopback()
    {
        if (!CanBindIpv6Loopback())
        {
            return;
        }

        await using var server = await UdpServerHarness.StartAsync(
            new Quote(1, "IPv6 only"),
            listenAddress: "::",
            dualMode: false);

        string ipv6Response = await ReadQuoteAsync(
            server.Port,
            IPAddress.IPv6Loopback);

        Assert.Equal("IPv6 only\r\n", ipv6Response);

        Exception? exception = await Record.ExceptionAsync(() =>
            ReadQuoteAsync(
                server.Port,
                IPAddress.Loopback,
                "hello",
                TimeSpan.FromMilliseconds(250)));

        Assert.True(
            exception is TimeoutException or SocketException,
            $"Expected no IPv4 response, but got {exception?.GetType().Name ?? "a response"}.");
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

    private static Task<string> ReadQuoteAsync(
        int port,
        string payload = "hello") =>
        ReadQuoteAsync(
            port,
            IPAddress.Loopback,
            payload,
            ShortTimeout);

    private static Task<string> ReadQuoteAsync(
        int port,
        IPAddress address) =>
        ReadQuoteAsync(
            port,
            address,
            "hello",
            ShortTimeout);

    private static async Task<string> ReadQuoteAsync(
        int port,
        IPAddress address,
        string payload,
        TimeSpan timeout)
    {
        using var client = new UdpClient(address.AddressFamily);

        byte[] requestBytes =
            Encoding.ASCII.GetBytes(payload);

        await client.SendAsync(
            requestBytes,
            requestBytes.Length,
            new IPEndPoint(address, port));

        Task<UdpReceiveResult> receiveTask =
            client.ReceiveAsync();

        UdpReceiveResult result =
            await receiveTask.WaitAsync(timeout);

        return Encoding.UTF8.GetString(result.Buffer);
    }

    private static bool CanBindIpv6Loopback()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return false;
        }

        try
        {
            using var socket =
                new Socket(
                    AddressFamily.InterNetworkV6,
                    SocketType.Dgram,
                    ProtocolType.Udp);

            socket.DualMode = false;
            socket.Bind(
                new IPEndPoint(
                    IPAddress.IPv6Loopback,
                    0));

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int GetAvailablePort(
        string listenAddress,
        bool dualMode)
    {
        IPAddress address = IPAddress.Parse(listenAddress);

        using var socket =
            new Socket(
                address.AddressFamily,
                SocketType.Dgram,
                ProtocolType.Udp);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            socket.DualMode = dualMode;
        }

        socket.Bind(
            new IPEndPoint(
                address,
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
            int maximumQuoteResponseCharacters = 512,
            string listenAddress = "127.0.0.1",
            bool dualMode = false)
        {
            int port = GetAvailablePort(
                listenAddress,
                dualMode);

            var missionControlClient =
                missionControl ?? new RecordingMissionControlClient();

            var options = new HappyQOTDOptions
            {
                ListenAddress = listenAddress,
                Port = port,
                DualMode = dualMode,
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
