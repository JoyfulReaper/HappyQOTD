using JoyfulReaperLib.MissionControl;
using HappyQOTD.Quotes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HappyQOTD.Tests.TestInfrastructure;

internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _adminKey;
    private readonly RecordingMissionControlClient _missionControlClient;
    private TestQuoteDatabase? _database;

    public TestWebApplicationFactory(
        string? adminKey = "test-admin-key",
        RecordingMissionControlClient? missionControlClient = null)
    {
        _adminKey = adminKey;
        _missionControlClient = missionControlClient ?? new RecordingMissionControlClient();
    }

    public RecordingMissionControlClient MissionControlClient => _missionControlClient;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _database = TestQuoteDatabase.CreateAsync().GetAwaiter().GetResult();

        var settings = new Dictionary<string, string?>
        {
            ["QOTD:QuoteConnectionString"] = _database.ConnectionString,
            ["QOTD:EnableTcpServer"] = "false",
            ["QOTD:Port"] = "0",
            ["MissionControl:Enabled"] = "false",
            ["QotdSecurity:AdminApiKey"] = _adminKey
        };

        foreach (var setting in settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQuoteRepository>();
            services.AddSingleton<IQuoteRepository>(
                _ => new SqliteRepository(_database.ConnectionString));

            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMissionControlClient>();
            services.AddSingleton<IMissionControlClient>(_missionControlClient);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _database?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
