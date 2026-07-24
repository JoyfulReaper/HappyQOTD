# HappyQOTD

HappyQOTD serves a daily quote through the classic Quote of the Day TCP protocol and an ASP.NET Core HTTP API. The daily quote is selected by UTC date and persisted in SQLite, so the TCP and HTTP services return the same quote.

> [!NOTE]
> The included seed database contains only a small set of SFW sample quotes. It is separate from the quote collection used by the public instance.

## Services

* TCP Quote of the Day server on port `17`
* ASP.NET Core Minimal API
* SQLite quote storage
* Optional Mission Control telemetry

## API

The local development URL is `http://localhost:5269`. The production deployment in the VPS compose stack listens on `http://127.0.0.1:5193`.

| Method   | Path                 | Authentication    | Description                                                                                    |
| -------- | -------------------- | ----------------- | ---------------------------------------------------------------------------------------------- |
| `GET`    | `/`                  | None              | Returns `HappyQOTD`.                                                                           |
| `GET`    | `/health/live`       | None              | Liveness check.                                                                                |
| `GET`    | `/health/ready`      | None              | Readiness check. Verifies that SQLite can be opened and queried.                               |
| `GET`    | `/api/quotes/today`  | None              | Returns the quote selected for the current UTC date. Returns `404` when no quote is available. |
| `GET`    | `/api/quotes/random` | None              | Returns a random active quote. Returns `404` when no quote is available.                       |
| `POST`   | `/api/quotes`        | `X-HappyQOTD-Key` | Creates one quote.                                                                             |
| `POST`   | `/api/quotes/batch`  | `X-HappyQOTD-Key` | Creates multiple quotes.                                                                       |
| `PUT`    | `/api/quotes/today`  | `X-HappyQOTD-Key` | Sets the current UTC day's quote.                                                              |
| `DELETE` | `/api/quotes/{id}`   | `X-HappyQOTD-Key` | Deletes a quote by numeric ID.                                                                 |

Write requests return `503` when no admin key is configured and `401` when the supplied key is missing or incorrect. Write endpoints are limited to 5 requests per minute. Read endpoints are limited to 120 requests per minute per client, except loopback clients.

### Quote request

`POST /api/quotes` accepts:

```json
{
  "text": "Readable code is a favor you leave for your future self.",
  "author": "",
  "source": "HappyQOTD original"
}
```

Quote validation rules:

* `text` is required and may contain up to 1,000 characters.
* `author` may contain up to 200 characters.
* `source` may contain up to 300 characters.

`POST /api/quotes/batch` accepts an array of the same request objects:

```json
[
  {
    "text": "First quote",
    "author": "Author",
    "source": "Example"
  },
  {
    "text": "Second quote"
  }
]
```

`PUT /api/quotes/today` accepts:

```json
{
  "quoteId": 4
}
```

`DELETE /api/quotes/{id}` returns `204` when the quote is deleted and `404` when the ID does not exist.

### Examples

Read today's quote:

```bash
curl http://localhost:5269/api/quotes/today
```

Create a quote:

```bash
curl -X POST http://localhost:5269/api/quotes \
  -H "Content-Type: application/json" \
  -H "X-HappyQOTD-Key: ${HAPPYQOTD_ADMIN_API_KEY}" \
  -d '{"text":"A useful quote","author":"An author","source":"Example"}'
```

Set today's quote:

```bash
curl -X PUT http://localhost:5269/api/quotes/today \
  -H "Content-Type: application/json" \
  -H "X-HappyQOTD-Key: ${HAPPYQOTD_ADMIN_API_KEY}" \
  -d '{"quoteId":4}'
```

Delete a quote:

```bash
curl -X DELETE http://localhost:5269/api/quotes/4 \
  -H "X-HappyQOTD-Key: ${HAPPYQOTD_ADMIN_API_KEY}"
```

When running in the Development environment, the app maps the generated OpenAPI document through ASP.NET Core OpenAPI.

## TCP QOTD

The TCP server sends the current quote and closes the connection:

```bash
nc 127.0.0.1 17
```

### Try the public instance

My live deployment can be queried directly:

```bash
nc qotd.kgivler.com 17
```

> [!WARNING]
> This instance uses my personal, unfiltered quote collection. Most quotes are programming-related, but some contain strong or obscene language or discuss suicide and other sensitive subjects.

The TCP listener uses `QOTD:ListenAddress` and `QOTD:Port`. Binding to port `17` may require `NET_BIND_SERVICE` or elevated privileges on Linux.

## Daily Selection

The quote of the day is keyed by the current UTC date. The repository selects and stores a quote when that date has no existing selection. Subsequent HTTP and TCP requests for that date return the stored selection, including after restarts.

## Configuration

The main configuration sections are:

| Setting                                | Description                                                    |
| -------------------------------------- | -------------------------------------------------------------- |
| `QotdSecurity:AdminApiKey`             | API key required by quote write endpoints.                     |
| `QOTD:ListenAddress`                   | TCP listener address.                                          |
| `QOTD:Port`                            | TCP listener port. Defaults to `17`.                           |
| `QOTD:EnableTcpServer`                 | Enables the shared hosted TCP server.                           |
| `QOTD:ApiBaseUrl`                      | Configured HTTP API base URL for integrations.                 |
| `QOTD:MaxConcurrentConnections`        | Maximum simultaneous TCP connections.                          |
| `QOTD:RequestTimeoutSeconds`           | Configured request-timeout value reserved for the TCP service. |
| `QOTD:TelemetryIgnoredRemoteAddresses` | TCP client addresses excluded from served-quote telemetry.     |
| `MissionControl:Enabled`               | Enables Mission Control telemetry.                             |
| `MissionControl:BaseUrl`               | Mission Control Gateway base URL.                              |
| `MissionControl:ApiKey`                | Mission Control source API key.                                |
| `MissionControl:TimeoutMilliseconds`   | Mission Control request timeout.                               |

Environment variables use double underscores, for example:

```text
QotdSecurity__AdminApiKey
QOTD__ListenAddress
QOTD__Port
QOTD__EnableTcpServer
QOTD__ApiBaseUrl
QOTD__MaxConcurrentConnections
QOTD__RequestTimeoutSeconds
QOTD__TelemetryIgnoredRemoteAddresses__0
MissionControl__Enabled
MissionControl__BaseUrl
MissionControl__ApiKey
MissionControl__TimeoutMilliseconds
```

Mission Control is disabled by default in `appsettings.json`. When enabled, its API key must match a configured Gateway event source and be at least 32 characters long.

## Local Development

Requirements:

* .NET 10 SDK
* Local JoyfulReaperLib packages available through `NuGet.config`

Restore and run:

```bash
dotnet restore
dotnet run --project HappyQOTD/HappyQOTD.csproj
```

The Development launch profile uses `http://localhost:5269` and enables the Development environment. Change `QOTD:Port` to a non-privileged port, such as `1717`, when port `17` cannot be bound.

Build the project:

```bash
dotnet build HappyQOTD/HappyQOTD.csproj
```

## Docker

Build the production image:

```bash
docker build -t happyqotd .
```

The VPS deployment uses host networking for HappyQOTD. Its relevant settings are:

```yaml
network_mode: host

environment:
  ASPNETCORE_URLS: http://127.0.0.1:5193
  QOTD__ListenAddress: 0.0.0.0
  QOTD__Port: 17
  MissionControl__Enabled: "true"
  MissionControl__BaseUrl: http://127.0.0.1:5190

depends_on:
  gateway:
    condition: service_healthy
```

The Gateway health check is `/health/ready`, which waits for its RabbitMQ connection and channel to be open. Use `docker compose up -d --build` after changing the image or application code. Use `--force-recreate` when container configuration or environment variables changed and the existing containers need to be replaced.

The container also needs a writable `/app/Data` volume for SQLite and `NET_BIND_SERVICE` when binding TCP port `17`:

```yaml
volumes:
  - /var/lib/happyqotd/data:/app/Data

cap_drop:
  - ALL

cap_add:
  - NET_BIND_SERVICE
```

## Mission Control Telemetry

When enabled, HappyQOTD publishes these event types:

* `happyqotd.service.started`
* `happyqotd.qotd.served`
* `happyqotd.api.qotd.served`
* `happyqotd.api.quote.added`
* `happyqotd.api.quotes.batch_added`
* `happyqotd.api.randomquote.served`
* `happyqotd.api.quote.deleted`

Telemetry failures are logged and do not normally prevent the HTTP API or TCP service from serving quotes. The startup event is attempted once when the shared hosted TCP server starts; restarting HappyQOTD retries it.

## Code Layout

* [`Program.cs`](./HappyQOTD/Program.cs) bootstraps the application.
* [`Extensions/HappyQotdApplicationExtensions.cs`](./HappyQOTD/Extensions/HappyQotdApplicationExtensions.cs) registers services and middleware.
* [`Routes/HappyQotdRouteExtensions.cs`](./HappyQOTD/Routes/HappyQotdRouteExtensions.cs) maps the HTTP endpoints and handlers.
* [`QOTDConnectionHandler.cs`](./HappyQOTD/QOTDConnectionHandler.cs) handles connections from the shared hosted TCP server.
* [`Data/QuoteDatabase.cs`](./HappyQOTD/Data/QuoteDatabase.cs) initializes the SQLite database.
* [`Quotes/SqliteRepository.cs`](./HappyQOTD/Quotes/SqliteRepository.cs) implements quote persistence.
* [`Events/QOTDJsonContext.cs`](./HappyQOTD/Events/QOTDJsonContext.cs) provides source-generated JSON metadata.

## License

HappyQOTD is licensed under the MIT License.

Copyright 2026 Kyle Givler.
