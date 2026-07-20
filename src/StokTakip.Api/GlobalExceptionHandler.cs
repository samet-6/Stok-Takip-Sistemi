using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            BadRequestException => (StatusCodes.Status400BadRequest, exception.Message),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, exception.Message),
            // Optimistic concurrency (stale xmin) — must precede the generic DbUpdateException arm.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Çakışma"),
            // Unique-violation safety net for the pre-check race window (Postgres 23505).
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } }
                => (StatusCodes.Status409Conflict, "Bu kayıt zaten mevcut."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.")
        };

        // Only unexpected errors are logged with the exception; never leaked to the client.
        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails { Status = status, Title = title };
        if (exception is DbUpdateConcurrencyException)
            problemDetails.Detail = "Kayıt başkası tarafından değiştirildi, sayfayı yenileyin.";
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
