using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Common;
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
        [FromQuery] int? productId,
        [FromQuery] string? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        // "Yapan" visibility: a Çalışan is locked to their own movements server-side
        // (any client-sent userId is ignored); an Admin may filter by any userId or none.
        var effectiveUserId = User.IsInRole("Admin") ? userId : User.FindFirstValue("sub");
        return Ok(await _stockMovementService.GetPagedAsync(productId, effectiveUserId, page, pageSize, ct));
    }

    // Stock movement is operational data — any authenticated user (Admin + Çalışan) may add it.
    // Class-level [Authorize] applies; no role restriction here (revizyon R2).
    [HttpPost]
    public async Task<ActionResult<StockMovementResponse>> Create(
        CreateStockMovementRequest request, CancellationToken ct)
    {
        // Identity comes from the token only — never accepted from the client.
        var userId = User.FindFirstValue("sub");
        if (userId is null)
            return Unauthorized();

        var response = await _stockMovementService.CreateAsync(request, userId, ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
