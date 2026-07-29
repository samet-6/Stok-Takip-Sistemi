using System.Reflection;
using StokTakip.Application.Common;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.UnitTests.Architecture;

/// <summary>
/// Onion's reference direction is a rule that lives in the csproj files, where nothing enforces
/// it at build time — a single PackageReference is enough to break it silently. These tests read
/// what the compiler actually emitted into each assembly.
/// </summary>
public sealed class LayerPurityTests
{
    /// <summary>
    /// Domain is framework-free POCO: only the base class library may appear.
    /// </summary>
    [Fact]
    public void Domain_hicbir_framework_paketine_referans_vermiyor()
    {
        var foreign = ReferencesOf(typeof(Product).Assembly)
            .Where(name => !IsBaseClassLibrary(name))
            .ToArray();

        Assert.True(
            foreign.Length == 0,
            "Domain framework'suz kalmali, su referanslar bulundu: " + string.Join(", ", foreign));
    }

    /// <summary>
    /// Application may know EF Core's abstractions (IAppDbContext exposes DbSet), but never a
    /// database provider — that is Infrastructure's job.
    /// </summary>
    [Fact]
    public void Application_veritabani_saglayicisina_referans_vermiyor()
    {
        var providers = ReferencesOf(typeof(PagedResult<>).Assembly)
            .Where(name => name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            providers.Length == 0,
            "Application saglayici-bagimsiz olmali, su referanslar bulundu: " + string.Join(", ", providers));
    }

    /// <summary>
    /// SignalR lives in the Api layer and nowhere else. Application only knows the framework-free
    /// IRealtimeNotifier; Infrastructure does not know about signalling at all. The temptation this
    /// guards against is concrete — the obvious place to reach for IHubContext is inside the service
    /// that just changed a row, and doing so would drag a transport into the business layer and put
    /// an awaitable hub call inside a transaction.
    /// </summary>
    [Theory]
    [InlineData(typeof(PagedResult<>))]
    [InlineData(typeof(StokTakip.Infrastructure.Data.AppDbContext))]
    public void Application_ve_Infrastructure_SignalR_a_referans_vermiyor(Type layerAnchor)
    {
        var signalR = ReferencesOf(layerAnchor.Assembly)
            .Where(name => name.Contains("SignalR", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            signalR.Length == 0,
            $"{layerAnchor.Assembly.GetName().Name} SignalR'a referans vermemeli, bulundu: "
            + string.Join(", ", signalR));
    }

    /// <summary>
    /// Referenced assemblies are what the compiler wrote into the metadata, i.e. what the code
    /// actually uses. An unused PackageReference would not show up here — and would also be
    /// harmless: the dependency starts to matter the moment a type from it is touched.
    /// </summary>
    private static IEnumerable<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static bool IsBaseClassLibrary(string assemblyName) =>
        assemblyName.StartsWith("System.", StringComparison.Ordinal)
        || assemblyName is "System" or "mscorlib" or "netstandard";
}
