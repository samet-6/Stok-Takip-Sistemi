using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Auth;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController : ControllerBase
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService) => _authService = authService;

    [HttpPost("change-password")]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(
        ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null)
            return Unauthorized();

        return Ok(await _authService.ChangePasswordAsync(userId, request, ct));
    }
}
