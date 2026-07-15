using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HappyQOTD.Quotes;

public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public SqliteHealthCheck(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            connectionString);

        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                new SqliteConnection(_connectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";

            var result = await command.ExecuteScalarAsync(
                cancellationToken);

            return result is 1L
                ? HealthCheckResult.Healthy(
                    "SQLite is reachable.")
                : HealthCheckResult.Unhealthy(
                    "SQLite probe returned an unexpected result.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "SQLite is not reachable.",
                exception);
        }
    }
}
