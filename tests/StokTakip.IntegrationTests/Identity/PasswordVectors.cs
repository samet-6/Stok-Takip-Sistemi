using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace StokTakip.IntegrationTests.Identity;

/// <summary>
/// Reads tests/fixtures/password-vectors.json — the same file the Vitest suite reads. Keeping
/// the rows outside both suites is the point: a shared file cannot be "updated on one side".
/// </summary>
internal static class PasswordVectors
{
    private const string FileName = "fixtures/password-vectors.json";

    private static readonly Lazy<Vector> Loaded = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        return JsonSerializer.Deserialize<Vector>(File.ReadAllText(path))
               ?? throw new InvalidOperationException($"Password vectors could not be read: {path}");
    });

    public static int RequiredLength => Loaded.Value.RequiredLength;

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var row in Loaded.Value.Cases)
            data.Add(row.Password, row.Valid);

        return data;
    }

    private sealed record Vector(
        [property: JsonPropertyName("requiredLength")] int RequiredLength,
        [property: JsonPropertyName("cases")] IReadOnlyList<Case> Cases);

    private sealed record Case(
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("why")] string Why);
}
