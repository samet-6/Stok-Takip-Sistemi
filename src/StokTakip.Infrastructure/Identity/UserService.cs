using Microsoft.AspNetCore.Identity;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.Users;

namespace StokTakip.Infrastructure.Identity;

// Admin-only employee (Çalışan) account management. Lives in Infrastructure because
// it drives ASP.NET Core Identity (UserManager) directly. The single seeded Admin is
// never managed through this service — admin-targeted edit/deactivate is rejected.
public sealed class UserService : IUserService
{
    private const string EmployeeRole = "User";
    private const string AdminRole = "Admin";

    private readonly UserManager<ApplicationUser> _userManager;

    public UserService(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<IReadOnlyList<UserListDto>> GetAllAsync(CancellationToken ct)
    {
        // Only Çalışan accounts; the single Admin is not in the User role, so it is excluded.
        var employees = await _userManager.GetUsersInRoleAsync(EmployeeRole);

        return employees
            .OrderBy(u => u.CreatedAt) // İşe Giriş ascending — first hired on top.
            .Select(u => new UserListDto(
                u.Id, u.Email!, u.FullName, new[] { EmployeeRole },
                u.IsActive, u.CreatedAt, u.DeactivatedAt))
            .ToList();
    }

    public async Task<UserListDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (await _userManager.FindByEmailAsync(request.Email) is not null)
            throw new ConflictException("Bu e-posta zaten kayıtlı");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FullName = request.FullName
            // IsActive + CreatedAt filled by DB defaults (see ApplicationUserConfiguration).
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Email duplication is pre-checked and required fields pass DataAnnotations
            // first, so CreateAsync failures here are password-policy violations. Surface
            // the full policy (not just the missing rule) under the password field.
            throw new BadRequestException(
                new Dictionary<string, string[]> { ["password"] = [PasswordPolicy.Message] });
        }

        await _userManager.AddToRoleAsync(user, EmployeeRole);

        return new UserListDto(
            user.Id, user.Email!, user.FullName, new[] { EmployeeRole },
            user.IsActive, user.CreatedAt, user.DeactivatedAt);
    }

    public async Task UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new NotFoundException("Kullanıcı bulunamadı");

        if (await _userManager.IsInRoleAsync(user, AdminRole))
            throw new BadRequestException("Admin hesabı yönetilemez.");

        if (!user.IsActive)
            throw new BadRequestException("Pasif kullanıcı düzenlenemez; önce işe geri alın.");

        // Pre-check duplicate email BEFORE any mutation so a conflict changes nothing.
        var emailChanged = !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged)
        {
            var existing = await _userManager.FindByEmailAsync(request.Email);
            if (existing is not null && existing.Id != user.Id)
                throw new ConflictException("Bu e-posta zaten kayıtlı");
        }

        // Password reset (optional) is applied first: a policy failure throws before any
        // other field is persisted. UserManager persists each step immediately. Admin has
        // no current password, and no token providers are configured, so we validate
        // against the policy up front (avoids a password-less window) then swap the hash;
        // AddPasswordAsync bumps the SecurityStamp used for forced logout.
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            foreach (var validator in _userManager.PasswordValidators)
            {
                var check = await validator.ValidateAsync(_userManager, user, request.Password);
                if (!check.Succeeded)
                    throw new BadRequestException(
                        new Dictionary<string, string[]> { ["password"] = [PasswordPolicy.Message] });
            }

            await _userManager.RemovePasswordAsync(user);
            var add = await _userManager.AddPasswordAsync(user, request.Password);
            if (!add.Succeeded)
                throw new BadRequestException(
                    new Dictionary<string, string[]> { ["password"] = [PasswordPolicy.Message] });
        }

        if (emailChanged)
        {
            var setEmail = await _userManager.SetEmailAsync(user, request.Email);
            if (!setEmail.Succeeded)
                throw new BadRequestException("Geçerli bir e-posta girin.");

            // Keep UserName + NormalizedUserName in sync so login-by-email keeps working.
            await _userManager.SetUserNameAsync(user, request.Email);

            // Force logout of the target: invalidate any existing token (ADR-0001).
            await _userManager.UpdateSecurityStampAsync(user);
        }

        user.FullName = request.FullName;
        await _userManager.UpdateAsync(user);
    }

    public async Task SetStatusAsync(string id, UpdateUserStatusRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new NotFoundException("Kullanıcı bulunamadı");

        if (await _userManager.IsInRoleAsync(user, AdminRole))
            throw new BadRequestException("Admin hesabı yönetilemez.");

        if (request.IsActive)
        {
            user.IsActive = true;
            user.DeactivatedAt = null; // İşe geri alındı — old password preserved.
        }
        else
        {
            user.IsActive = false;
            user.DeactivatedAt = DateTime.UtcNow; // İşten çıkarıldı.
        }

        await _userManager.UpdateAsync(user);
    }
}
