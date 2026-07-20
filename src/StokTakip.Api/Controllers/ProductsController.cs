using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Common;
using StokTakip.Application.Products;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService) => _productService = productService;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductListDto>>> GetAll(
        [FromQuery] ProductQuery query, CancellationToken ct)
        => Ok(await _productService.GetPagedAsync(query, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(int id, CancellationToken ct)
        => Ok(await _productService.GetByIdAsync(id, ct));

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ProductDetailDto>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null)
            return Unauthorized();

        var dto = await _productService.CreateAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateProductRequest request, CancellationToken ct)
    {
        await _productService.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var soft = await _productService.DeleteAsync(id, ct);
        return soft is null ? NoContent() : Ok(soft);
    }
}
