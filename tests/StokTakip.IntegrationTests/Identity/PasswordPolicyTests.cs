using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StokTakip.Infrastructure.Identity;
using Xunit;

namespace StokTakip.IntegrationTests.Identity;

/// <summary>
/// The policy is declared once and applied by IdentityOptionsSetup. These read the options out
/// of the <b>running application</b> rather than the class alone, and that is the whole point:
/// the defect this closes was not a wrongly written class but a registered class that never ran
/// (TurkishIdentityErrorDescriber). A test that only asked PasswordPolicy what it holds would
/// pass just as happily while nothing wired it to Identity.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PasswordPolicyTests
{
    private readonly TestDatabaseFixture _db;

    public PasswordPolicyTests(TestDatabaseFixture db) => _db = db;

    [Fact]
    public void Identity_secenekleri_politikadan_besleniyor()
    {
        using var scope = _db.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(PasswordPolicy.RequiredLength, options.Password.RequiredLength);
        Assert.Equal(PasswordPolicy.RequireUppercase, options.Password.RequireUppercase);
        Assert.Equal(PasswordPolicy.RequireLowercase, options.Password.RequireLowercase);
        Assert.Equal(PasswordPolicy.RequireDigit, options.Password.RequireDigit);
        Assert.Equal(PasswordPolicy.RequireNonAlphanumeric, options.Password.RequireNonAlphanumeric);
    }

    /// <summary>The sentence is built from the numbers, so it cannot age behind them.</summary>
    [Fact]
    public void Politika_metni_uzunluk_sayisini_iceriyor() =>
        Assert.Contains($"en az {PasswordPolicy.RequiredLength} karakter", PasswordPolicy.Message);

    public static TheoryData<string, bool> Vectors => PasswordVectors.Cases();

    /// <summary>
    /// The vectors are the contract between the two implementations of one rule. This half runs
    /// them through the real Identity validators — the ones that actually decide whether an
    /// account can be created — while web/src/test/passwordPolicy.test.ts runs the same rows
    /// through the zod schema. Neither side can move without the other going red.
    /// </summary>
    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task Vektorler_Identity_dogrulayicilariyla_ortusuyor(string password, bool expected)
    {
        using var scope = _db.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Vektör", Email = "vektor@stok.local" };

        var accepted = true;
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            accepted &= result.Succeeded;
        }

        Assert.Equal(expected, accepted);
    }

    /// <summary>The number travels in the file too, so the two sides cannot disagree on it.</summary>
    [Fact]
    public void Vektor_dosyasindaki_uzunluk_politikayla_ayni() =>
        Assert.Equal(PasswordPolicy.RequiredLength, PasswordVectors.RequiredLength);
}
