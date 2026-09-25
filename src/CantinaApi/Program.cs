using System.Text.Json.Serialization;
using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Data;
using CantinaApi.Features.Auth;
using CantinaApi.Features.MenuItems;
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

// The correlation id is the single request id; the per-request trace id would make identical errors differ byte for byte.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.Remove("traceId");
        context.ProblemDetails.Extensions.TryAdd("correlationId", context.HttpContext.TraceIdentifier);
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();

// Enums travel as names; numbers are rejected so undefined values cannot slip through.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

// Binding failures return a bare 400 in every environment, which status code pages turn into ProblemDetails.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<SeedOptions>().Bind(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHybridCache();
builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
builder.Services.AddCantinaAuth(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<CantinaDbContext>("database");

var app = builder.Build();

await app.MigrateAndSeedDatabaseAsync();

// Runs ahead of the pipeline below so every request log line and response, including errors, carries the id.
app.UseMiddleware<CorrelationIdMiddleware>();

// Order matters: the exception handler must wrap everything, status code pages give bare error codes a ProblemDetails body, request logging must see failures and timings, and routing must pick the endpoint before rate limiting and auth read its metadata.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// Every endpoint needs a token unless it opts out here or in its feature file.
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.WriteAsync }).AllowAnonymous();
app.MapAuthEndpoints();
app.MapMenuItemEndpoints();

await app.RunAsync();
