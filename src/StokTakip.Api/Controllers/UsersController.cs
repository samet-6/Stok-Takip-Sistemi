using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Users;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService) => _userService = userService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserListDto>>> GetAll(CancellationToken ct)
        => Ok(await _userService.GetAllAsync(ct));

    [HttpPost]
    public async Task<ActionResult<UserListDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var dto = await _userService.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateUserRequest request, CancellationToken ct)
    {
        await _userService.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> SetStatus(string id, UpdateUserStatusRequest request, CancellationToken ct)
    {
        await _userService.SetStatusAsync(id, request, ct);
        return NoContent();
    }
}
