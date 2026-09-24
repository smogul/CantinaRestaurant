using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace CantinaApi.Common;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;

        // Registered before the response starts so error responses carry the header too.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Only short, header-safe caller ids are trusted so clients cannot inject noise into logs.
    private static string ReadOrCreate(StringValues header)
    {
        var value = header.ToString();
        var isValid = value.Length is > 0 and <= MaxLength
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

        return isValid ? value : Guid.NewGuid().ToString();
    }
}
