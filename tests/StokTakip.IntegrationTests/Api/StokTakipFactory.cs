using Microsoft.AspNetCore.Mvc.Testing;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Boots the real application in memory: real DI container, real middleware pipeline, real
/// authentication. Only the configuration is replaced, so what the tests exercise is the
/// application as shipped.
/// </summary>
public sealed class StokTakipFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Credentials the seeder plants in the throwaway test database. They are not secrets: the
    /// database is dropped at the start of every run, and the tests have to know them to log in.
    /// Kept here rather than in configuration so a test run needs exactly one environment
    /// variable (the connection string) instead of five.
    /// </summary>
    public const string AdminEmail = "admin@stok.local";
    public const string AdminPassword = "TestAdmin!2026";
    public const string UserEmail = "user@stok.local";
    public const string UserPassword = "TestUser!2026";

    public StokTakipFactory(string connectionString)
    {
        // Configuration is injected through environment variables rather than
        // ConfigureAppConfiguration, and that is not a style choice. Under the minimal hosting
        // model Program.cs reads builder.Configuration while it runs — before the factory's
        // ConfigureAppConfiguration callbacks fire — so anything added there arrives too late and
        // startup dies with "Jwt configuration section is missing". Environment variables are one
        // of the default configuration sources and are already in place when the host is built.
        // Double underscore is the section separator ("Jwt__Key" -> "Jwt:Key").
        SetVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        SetVariable("ConnectionStrings__Default", connectionString);

        // The application signs its own tokens with this key, so the tests never forge one — they
        // log in through the real endpoint like any client.
        SetVariable("Jwt__Issuer", "StokTakip.Tests");
        SetVariable("Jwt__Audience", "StokTakip.Tests");
        SetVariable("Jwt__Key", "test-only-signing-key-not-used-anywhere-else-0123456789");
        SetVariable("Jwt__ExpiryHours", "8");

        SetVariable("Seed__AdminPassword", AdminPassword);
        SetVariable("Seed__UserPassword", UserPassword);
    }

    private static void SetVariable(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value);
}
