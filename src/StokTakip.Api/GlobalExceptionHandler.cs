using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Common.Exceptions;

namespace StokTakip.Api;

// Single funnel: Application exceptions → RFC 7807 ProblemDetails.
// F3'te tüm servisler aynısını kullanacak.
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ConflictException => (StatusCodes.Status409Conflict, exception.Message),
            BadRequestException => (StatusCodes.Status400BadRequest, exception.Message),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.")
        };

        // Only unexpected errors are logged with the exception; never leaked to the client.
        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails { Status = status, Title = title };
        if (exception is BadRequestException { Errors.Count: > 0 } badRequest)
            problemDetails.Extensions["errors"] = badRequest.Errors;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
