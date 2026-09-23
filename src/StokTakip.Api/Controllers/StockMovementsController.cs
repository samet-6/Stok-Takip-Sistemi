using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.StockMovements;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/stock-movements")]
[Authorize]
public sealed class StockMovementsController : ControllerBase
{
    private readonly IStockMovementService _stockMovementService;

    public StockMovementsController(IStockMovementService stockMovementService)
        => _stockMovementService = stockMovementService;

    [HttpGet]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> GetPaged(
        [FromQuery] StockMovementQuery query, CancellationToken ct = default)
    {
        // "Yapan" visibility: a Çalışan is locked to their own movements server-side
        // (any client-sent userId is ignored); an Admin may filter by any userId or none.
        var effectiveUserId = User.IsInRole("Admin") ? query.UserId : User.FindFirstValue("sub");
        var effective = query with { UserId = effectiveUserId };
        return Ok(await _stockMovementService.GetPagedAsync(effective, ct));
    }

    // Stock movement is operational data — any authenticated user (Admin + Çalışan) may add it.
    // Class-level [Authorize] applies; no role restriction here.
    [HttpPost]
    public async Task<ActionResult<StockMovementResponse>> Create(
        CreateStockMovementRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        // Identity comes from the token only — never accepted from the client.
        var userId = User.FindFirstValue("sub");
        if (userId is null)
            return Unauthorized();

        // Required, not optional (D27): the guarantee is only as strong as the client that
        // remembers to send a key. The empty GUID is refused too — every client that forgot to
        // generate one would share it, and their movements would collide.
        if (!Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty)
            throw new IdempotencyKeyRequiredException();

        var response = await _stockMovementService.CreateAsync(request, userId, key, ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
