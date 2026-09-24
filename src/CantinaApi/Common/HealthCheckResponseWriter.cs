using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CantinaApi.Common;

public static class HealthCheckResponseWriter
{
    // Exception details are left out so the public endpoint does not leak internals.
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new HealthResponse(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new HealthCheckEntry(
                    entry.Value.Status.ToString(),
                    entry.Value.Description,
                    entry.Value.Duration.TotalMilliseconds)));

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    private sealed record HealthResponse(string Status, double TotalDurationMs, Dictionary<string, HealthCheckEntry> Checks);

    private sealed record HealthCheckEntry(string Status, string? Description, double DurationMs);
}
