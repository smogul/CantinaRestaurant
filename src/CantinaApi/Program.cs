using CantinaApi.Common;
using CantinaApi.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Resolved per context so environment variables and test overrides are always honoured.
builder.Services.AddDbContext<CantinaDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("CantinaDb")
        ?? throw new InvalidOperationException("Connection string 'CantinaDb' is not configured.")));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions.TryAdd("correlationId", context.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHybridCache();
builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<CantinaDbContext>("database");

var app = builder.Build();

await app.MigrateAndSeedDatabaseAsync();

// Runs ahead of the pipeline below so every request log line and response, including errors, carries the id.
app.UseMiddleware<CorrelationIdMiddleware>();

// Order matters: the exception handler must wrap everything, request logging must see failures and timings, and routing must pick the endpoint before rate limiting and auth read its metadata.
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.WriteAsync });

await app.RunAsync();
