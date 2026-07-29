using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Every endpoint, seen from all three sides. The table is the readable form of the
/// authorisation rule in the project charter; the coverage test below makes sure it stays
/// complete, so an endpoint added without a decision about who may call it breaks the build
/// rather than shipping open.
/// </summary>
/// <remarks>
/// Only the gate is under test here, never the work behind it: every request either targets a
/// row that does not exist or carries an empty body, so an allowed call is a no-op. That is why
/// <see cref="Access.Allowed"/> means "anything but 401/403" — pinning exact success codes would
/// re-test validation and turn this table into a maintenance burden.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AuthorizationMatrixTests
{
    private enum Access
    {
        Allowed,
        Unauthorized,
        Forbidden
    }

    private sealed record ApiEndpoint(
        string Method,
        string Template,
        string Url,
        Access Anonymous,
        Access Calisan,
        Access Admin,
        bool SendsBody = false);

    private const string MissingId = "999999";
    private const string MissingUserId = "yok-boyle-bir-kullanici";

    private static readonly ApiEndpoint[] Matrix =
    [
        // Auth — the only anonymous door in the application.
        new("POST", "api/auth/login", "/api/auth/login", Access.Allowed, Access.Allowed, Access.Allowed, SendsBody: true),
        new("GET", "api/auth/me", "/api/auth/me", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("POST", "api/auth/hub-ticket", "/api/auth/hub-ticket", Access.Unauthorized, Access.Allowed, Access.Allowed),

        // Own account — every signed-in user manages their own password.
        new("POST", "api/account/change-password", "/api/account/change-password", Access.Unauthorized, Access.Allowed, Access.Allowed, SendsBody: true),

        // Catalogue: reading is open to employees, writing is configuration and stays with the admin.
        new("GET", "api/categories", "/api/categories", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("GET", "api/categories/{id:int}", $"/api/categories/{MissingId}", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("POST", "api/categories", "/api/categories", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("PUT", "api/categories/{id:int}", $"/api/categories/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("DELETE", "api/categories/{id:int}", $"/api/categories/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed),

        new("GET", "api/suppliers", "/api/suppliers", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("GET", "api/suppliers/{id:int}", $"/api/suppliers/{MissingId}", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("POST", "api/suppliers", "/api/suppliers", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("PUT", "api/suppliers/{id:int}", $"/api/suppliers/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("DELETE", "api/suppliers/{id:int}", $"/api/suppliers/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed),

        new("GET", "api/products", "/api/products", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("GET", "api/products/summary", "/api/products/summary", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("GET", "api/products/{id:int}", $"/api/products/{MissingId}", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("POST", "api/products", "/api/products", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("PUT", "api/products/{id:int}", $"/api/products/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("DELETE", "api/products/{id:int}", $"/api/products/{MissingId}", Access.Unauthorized, Access.Forbidden, Access.Allowed),

        // Stock movement is operational data: an employee's daily work, not configuration.
        new("GET", "api/stock-movements", "/api/stock-movements", Access.Unauthorized, Access.Allowed, Access.Allowed),
        new("POST", "api/stock-movements", "/api/stock-movements", Access.Unauthorized, Access.Allowed, Access.Allowed, SendsBody: true),

        // User management and notifications are admin-only from top to bottom.
        new("GET", "api/users", "/api/users", Access.Unauthorized, Access.Forbidden, Access.Allowed),
        new("POST", "api/users", "/api/users", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("PUT", "api/users/{id}", $"/api/users/{MissingUserId}", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),
        new("PATCH", "api/users/{id}", $"/api/users/{MissingUserId}", Access.Unauthorized, Access.Forbidden, Access.Allowed, SendsBody: true),

        new("GET", "api/notifications", "/api/notifications", Access.Unauthorized, Access.Forbidden, Access.Allowed),
        new("POST", "api/notifications/{id:int}/read", $"/api/notifications/{MissingId}/read", Access.Unauthorized, Access.Forbidden, Access.Allowed),
        new("POST", "api/notifications/read-all", "/api/notifications/read-all", Access.Unauthorized, Access.Forbidden, Access.Allowed)
    ];

    private readonly TestDatabaseFixture _db;

    public AuthorizationMatrixTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Public self-registration was removed in the corporate revision: accounts are opened by
    /// the admin. A route left behind would let anyone create themselves an account.
    /// </summary>
    [Fact]
    public async Task Register_ucu_kaldirildi_404_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "yeni@stok.local", password = "Herhangi!2026", fullName = "Yeni" },
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonim_yetki_matrisi()
    {
        await AssertMatrixAsync(_db.Factory.CreateClient(), endpoint => endpoint.Anonymous, "anonim");
    }

    [Fact]
    public async Task Calisan_yetki_matrisi()
    {
        using var client = await _db.Factory.AsCalisanAsync(Ct);

        await AssertMatrixAsync(client, endpoint => endpoint.Calisan, "Çalışan");
    }

    [Fact]
    public async Task Admin_yetki_matrisi()
    {
        using var client = await _db.Factory.AsAdminAsync(Ct);

        await AssertMatrixAsync(client, endpoint => endpoint.Admin, "Admin");
    }

    /// <summary>
    /// The table only means something while it is complete. Compared against the application's
    /// own action table in both directions: a new endpoint with no row fails here, and so does
    /// a row left behind after a route was renamed.
    /// </summary>
    [Fact]
    public void Matris_rota_tablosundaki_tum_uclari_kapsiyor()
    {
        var routed = _db.Factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .SelectMany(action => HttpMethodsOf(action)
                .Select(method => $"{method} {action.AttributeRouteInfo?.Template}"))
            .ToHashSet();

        Assert.NotEmpty(routed);

        var covered = Matrix.Select(endpoint => $"{endpoint.Method} {endpoint.Template}").ToHashSet();

        var missing = routed.Except(covered).Order().ToArray();
        var stale = covered.Except(routed).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            "Yetki matrisinde satiri olmayan uclar: " + string.Join(", ", missing));
        Assert.True(
            stale.Length == 0,
            "Matriste olup rota tablosunda olmayan satirlar: " + string.Join(", ", stale));
    }

    private static async Task AssertMatrixAsync(
        HttpClient client, Func<ApiEndpoint, Access> expectationOf, string role)
    {
        using (client)
        {
            var failures = new List<string>();

            foreach (var endpoint in Matrix)
            {
                var expected = expectationOf(endpoint);
                var actual = await SendAsync(client, endpoint);

                if (!Matches(expected, actual))
                    failures.Add($"{endpoint.Method} {endpoint.Url}: {expected} bekleniyordu, {(int)actual} geldi");
            }

            Assert.True(
                failures.Count == 0,
                $"{role} için {failures.Count} uçta yetki beklentisi tutmadı:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));
        }
    }

    private static bool Matches(Access expected, HttpStatusCode actual) => expected switch
    {
        Access.Unauthorized => actual == HttpStatusCode.Unauthorized,
        Access.Forbidden => actual == HttpStatusCode.Forbidden,
        // Deliberately loose: the request got past the gate, which is all this table claims.
        Access.Allowed => actual is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
        _ => throw new ArgumentOutOfRangeException(nameof(expected))
    };

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, ApiEndpoint endpoint)
    {
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Url);

        if (endpoint.SendsBody)
            request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request, Ct);

        return response.StatusCode;
    }

    private static IEnumerable<string> HttpMethodsOf(ControllerActionDescriptor action) =>
        action.ActionConstraints?
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
        ?? [];
}
