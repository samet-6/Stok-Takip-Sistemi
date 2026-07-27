using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StokTakip.Application.Common.Exceptions;

namespace StokTakip.Api;

// Single funnel: Application exceptions → RFC 7807 ProblemDetails.
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
        // 'code' is a stable, machine-readable discriminator (RFC 7807 extension) so
        // clients branch on it instead of parsing human-readable Turkish titles.
        var (status, title, code) = exception switch
        {
            ConflictException => (StatusCodes.Status409Conflict, exception.Message, "conflict"),
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message, "not_found"),
            BadRequestException => (StatusCodes.Status400BadRequest, exception.Message, "bad_request"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, exception.Message, "unauthorized"),
            // Optimistic concurrency (stale xmin) — must precede the generic DbUpdateException arm.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Çakışma", "concurrency_conflict"),
            // Unique-violation safety net for the pre-check race window (Postgres 23505).
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } }
                => (StatusCodes.Status409Conflict, "Bu kayıt zaten mevcut.", "conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.", "internal_error")
        };

        // Only unexpected errors are logged with the exception; never leaked to the client.
        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails { Status = status, Title = title };
        problemDetails.Extensions["code"] = code;
        if (exception is DbUpdateConcurrencyException)
            problemDetails.Detail = "Kayıt başkası tarafından değiştirildi, sayfayı yenileyin.";
        if (exception is BadRequestException { FieldErrors: { Count: > 0 } } badRequest)
            problemDetails.Extensions["errors"] = badRequest.FieldErrors;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
