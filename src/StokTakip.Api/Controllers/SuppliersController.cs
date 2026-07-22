using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Suppliers;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public sealed class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService) => _supplierService = supplierService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetAll(CancellationToken ct)
    {
        var suppliers = await _supplierService.GetAllAsync(ct);
        return Ok(User.IsInRole("Admin") ? suppliers : suppliers.Select(Redact).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupplierDto>> GetById(int id, CancellationToken ct)
    {
        // Non-null here: the service throws NotFoundException when the id is missing.
        var supplier = await _supplierService.GetByIdAsync(id, ct);
        return Ok(User.IsInRole("Admin") ? supplier : Redact(supplier!));
    }

    // Supplier contact details (email/phone/address) are admin-only. GET is open to every
    // authenticated user, so the fields are stripped here for non-admins — this is the real
    // boundary; the frontend hiding is only cosmetic.
    private static SupplierDto Redact(SupplierDto s) =>
        s with { ContactEmail = string.Empty, Phone = null, Address = null };

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierRequest request, CancellationToken ct)
    {
        var dto = await _supplierService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateSupplierRequest request, CancellationToken ct)
    {
        await _supplierService.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _supplierService.DeleteAsync(id, ct);
        return NoContent();
    }
}
