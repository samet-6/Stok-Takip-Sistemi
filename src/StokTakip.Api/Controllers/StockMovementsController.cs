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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => Ok(await _stockMovementService.GetPagedAsync(productId, page, pageSize, ct));

    [Authorize(Roles = "Admin")]
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
