using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace HappyQOTD.Security;

public sealed class ApiKeyEndpointFilter(
    IOptions<QotdSecurityOptions> options)
    : IEndpointFilter
{
    public const string HeaderName = "X-HappyQOTD-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var expectedKey = options.Value.AdminApiKey;

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            return Results.Problem(
                title: "Quote administration is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(
                HeaderName,
                out var suppliedValues))
        {
            return Results.Unauthorized();
        }

        var suppliedKey = suppliedValues.ToString();

        if (!KeysMatch(expectedKey, suppliedKey))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static bool KeysMatch(
        string expected,
        string supplied)
    {
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expected));

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(supplied));

        return CryptographicOperations.FixedTimeEquals(
            expectedHash,
            suppliedHash);
    }
}