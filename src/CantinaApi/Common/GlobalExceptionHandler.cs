using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CantinaApi.Common;

// Serilog request logging already records the exception with request context, so it is not logged again here.
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, IHostEnvironment environment) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Malformed requests surface as BadHttpRequestException and should stay 4xx.
        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails { Status = status };

        // Exception details help locally but leak internals anywhere else.
        if (environment.IsDevelopment())
        {
            problemDetails.Detail = exception.Message;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        httpContext.Response.StatusCode = status;
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }
}
