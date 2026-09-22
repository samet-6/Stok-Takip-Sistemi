using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Api.Realtime;
using StokTakip.Infrastructure.Auth;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Api;

/// <summary>
/// Everything that happens to a bearer token between arrival and acceptance, plus how the two
/// refusals are reported. Lifted out of Program.cs for the same reason <see cref="LoginRateLimit"/>
/// was: this is a policy with its own rules, while Program.cs is a wiring list.
/// </summary>
public static class JwtBearerEventHandlers
{
    public static JwtBearerEvents Create() => new()
    {
        OnMessageReceived = ReadHubTicketFromQueryString,
        OnTokenValidated = ValidateSessionAsync,
        OnChallenge = WriteUnauthorizedAsync,
        OnForbidden = WriteForbiddenAsync
    };

    /// <summary>
    /// The browser's WebSocket API cannot set an Authorization header on the handshake, so the
    /// hub identity must travel in the query string. Accepted only under /hubs — and
    /// <see cref="ValidateSessionAsync"/> makes sure the only thing that works there is a
    /// short-lived ticket, never the session token.
    /// </summary>
    private static Task ReadHubTicketFromQueryString(MessageReceivedContext context)
    {
        if (context.HttpContext.Request.Path.StartsWithSegments(HubRoutes.Prefix))
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
                context.Token = accessToken;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Per-request session validation: the token's SecurityStamp must match the DB and the user
    /// must still be active — else the token is rejected (401 via OnChallenge). This is what makes
    /// admin password reset / email change / deactivation take effect instantly instead of waiting
    /// for token expiry.
    /// </summary>
    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;

        // Two-way scope fence: a hub ticket is only good for /hubs, and /hubs accepts nothing
        // else. The second half is what keeps the 8-hour session token out of query strings —
        // and therefore out of access logs — as a server-enforced invariant rather than
        // client-side good manners. Checked before the DB round trip so a misrouted token
        // costs no query.
        var isHubPath = context.HttpContext.Request.Path.StartsWithSegments(HubRoutes.Prefix);
        var isHubTicket =
            principal.FindFirstValue(TokenService.ScopeClaimType) == TokenService.HubScope;

        if (isHubPath != isHubTicket)
        {
            context.Fail("Bilet bu yol için geçerli değil.");
            return;
        }

        var userId = principal.FindFirstValue("sub");
        var tokenStamp = principal.FindFirstValue(TokenService.SecurityStampClaimType);

        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);

        if (user is null || !user.IsActive || user.SecurityStamp != tokenStamp)
            context.Fail("Oturum geçersiz.");
    }

    private static Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        // Suppresses the handler's own WWW-Authenticate response, so the body written below is
        // what the caller actually receives.
        context.HandleResponse();

        return WriteProblemAsync(
            context.Response, StatusCodes.Status401Unauthorized, "Kimlik doğrulama gerekli.");
    }

    private static Task WriteForbiddenAsync(ForbiddenContext context)
        => WriteProblemAsync(
            context.Response, StatusCodes.Status403Forbidden, "Bu işlem için yetkiniz yok.");

    private static Task WriteProblemAsync(HttpResponse response, int status, string title)
    {
        response.StatusCode = status;

        // The content type has to travel with the write: WriteAsJsonAsync sets its own
        // ("application/json") and would overwrite anything assigned to the response
        // beforehand. Every other error in the API is RFC 7807, these two included.
        return response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title },
            options: null,
            contentType: "application/problem+json");
    }
}
