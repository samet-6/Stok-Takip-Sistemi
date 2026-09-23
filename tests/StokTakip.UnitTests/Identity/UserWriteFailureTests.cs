using Microsoft.AspNetCore.Identity;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Infrastructure.Identity;
using Xunit;

namespace StokTakip.UnitTests.Identity;

/// <summary>
/// UserService checks for a taken address before it writes, so Identity reports a duplicate only
/// when two admins race for the same address — a window no integration test can hit on demand.
/// What the loser of that race is told is decided here, so it is pinned here: the same 409 the
/// pre-check gives, never a password complaint or an "invalid address" about a valid one.
/// </summary>
public class UserWriteFailureTests
{
    [Theory]
    [InlineData(nameof(IdentityErrorDescriber.DuplicateEmail))]
    [InlineData(nameof(IdentityErrorDescriber.DuplicateUserName))]
    public void Yinelenen_eposta_olusturmada_409(string code)
    {
        var error = UserWriteFailure.ForCreate(Failed(code));

        var conflict = Assert.IsType<ConflictException>(error);
        Assert.Equal("Bu e-posta zaten kayıtlı", conflict.Message);
    }

    [Theory]
    [InlineData(nameof(IdentityErrorDescriber.DuplicateEmail))]
    [InlineData(nameof(IdentityErrorDescriber.DuplicateUserName))]
    public void Yinelenen_eposta_duzenlemede_409(string code)
    {
        var error = UserWriteFailure.ForUpdate(Failed(code));

        var conflict = Assert.IsType<ConflictException>(error);
        Assert.Equal("Bu e-posta zaten kayıtlı", conflict.Message);
    }

    [Fact]
    public void Olusturmada_kalan_hatalar_parola_alanina_yaziliyor()
    {
        var error = UserWriteFailure.ForCreate(Failed(nameof(IdentityErrorDescriber.PasswordTooShort)));

        var badRequest = Assert.IsType<BadRequestException>(error);
        Assert.Equal([PasswordPolicy.Message], badRequest.FieldErrors!["password"]);
    }

    [Fact]
    public void Duzenlemede_gecersiz_eposta_email_alanina_yaziliyor()
    {
        var error = UserWriteFailure.ForUpdate(Failed(nameof(IdentityErrorDescriber.InvalidEmail)));

        var badRequest = Assert.IsType<BadRequestException>(error);
        Assert.Equal(["Geçerli bir e-posta girin."], badRequest.FieldErrors!["email"]);
    }

    private static IdentityResult Failed(string code) =>
        IdentityResult.Failed(new IdentityError { Code = code, Description = code });
}
