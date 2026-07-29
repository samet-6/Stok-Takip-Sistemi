using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Reads the application's own action table instead of re-deriving routes from attributes by
/// hand: a hand-rolled route builder that misreads a template would pass while the real API
/// exposes something else entirely.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ApiSurfaceTests
{
    private const string StockMovementsRoute = "api/stock-movements";

    private static readonly string EntityNamespace = typeof(Product).Namespace!;

    private readonly TestDatabaseFixture _db;

    public ApiSurfaceTests(TestDatabaseFixture db) => _db = db;

    private IReadOnlyList<ControllerActionDescriptor> Actions =>
        _db.Factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToArray();

    /// <summary>
    /// Entities never leave the API: the response shape is a DTO's job. Only declared return
    /// types can be checked — an action returning IActionResult is opaque to this test, which is
    /// why the typed ActionResult&lt;T&gt; form is the convention here.
    /// </summary>
    [Fact]
    public void Hicbir_action_domain_entity_dondurmuyor()
    {
        var actions = Actions;
        Assert.NotEmpty(actions);

        var leaks = actions
            .SelectMany(action => Unwrap(action.MethodInfo.ReturnType)
                .Where(type => type.Namespace == EntityNamespace)
                .Select(type => $"{action.ControllerName}.{action.ActionName} -> {type.Name}"))
            .ToArray();

        Assert.True(
            leaks.Length == 0,
            "Entity donen action'lar: " + string.Join(", ", leaks));
    }

    /// <summary>
    /// Stock movements are append-only: the audit trail is the point, so there is no way to
    /// rewrite or erase a movement through the API.
    /// </summary>
    [Fact]
    public void Stok_hareketlerinde_yazma_disinda_ucu_yok()
    {
        var methods = Actions
            .Where(action => action.AttributeRouteInfo?.Template?
                .StartsWith(StockMovementsRoute, StringComparison.Ordinal) == true)
            .SelectMany(HttpMethodsOf)
            .Distinct()
            .ToArray();

        // Guards the test itself: if the route moved or the metadata changed shape, the list
        // would come back empty and "no PUT/DELETE found" would be vacuously true.
        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);

        Assert.DoesNotContain("PUT", methods);
        Assert.DoesNotContain("PATCH", methods);
        Assert.DoesNotContain("DELETE", methods);
    }

    private static IEnumerable<string> HttpMethodsOf(ControllerActionDescriptor action) =>
        action.ActionConstraints?
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
        ?? [];

    /// <summary>
    /// Walks the whole generic tree: an entity hidden in Task&lt;ActionResult&lt;List&lt;Product&gt;&gt;&gt;
    /// is just as much a leak as a bare return type.
    /// </summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
        {
            foreach (var nested in Unwrap(element))
                yield return nested;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Unwrap(argument))
                yield return nested;
        }
    }
}
