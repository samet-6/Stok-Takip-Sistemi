using Microsoft.AspNetCore.Identity;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.Users;

namespace StokTakip.Infrastructure.Identity;

// Admin-only employee (Çalışan) account management on top of UserManager. The single
// seeded Admin is never managed here — admin-targeted edit/deactivate is rejected.
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

        // Optional password reset. The policy is checked before anything is touched, then the
        // new hash is only staged in memory. A fresh SecurityStamp invalidates the target's
        // existing tokens (forced logout).
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            foreach (var validator in _userManager.PasswordValidators)
            {
                var check = await validator.ValidateAsync(_userManager, user, request.Password);
                if (!check.Succeeded)
                    throw new BadRequestException(
                        new Dictionary<string, string[]> { ["password"] = [PasswordPolicy.Message] });
            }

            user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, request.Password);
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        if (emailChanged)
        {
            // UserName tracks Email so login-by-email keeps working; both normalized forms are
            // what Identity actually looks up by. Changing the email also forces a logout.
            user.Email = request.Email;
            user.NormalizedEmail = _userManager.NormalizeEmail(request.Email);
            user.UserName = request.Email;
            user.NormalizedUserName = _userManager.NormalizeName(request.Email);
            user.SecurityStamp = Guid.NewGuid().ToString();
            // An address the admin typed counts as verified, same as at account creation.
            user.EmailConfirmed = true;
        }

        user.FullName = request.FullName;

        // Single write for every field above: the account can never be left half-updated —
        // in particular never without a password hash.
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
            throw ToBadRequest(update);
    }

    // UpdateAsync runs Identity's user validator, whose failures are about the email or the
    // username derived from it; anything else is reported without blaming a specific field.
    private static BadRequestException ToBadRequest(IdentityResult result)
    {
        string[] emailCodes =
        [
            nameof(IdentityErrorDescriber.InvalidEmail),
            nameof(IdentityErrorDescriber.DuplicateEmail),
            nameof(IdentityErrorDescriber.InvalidUserName),
            nameof(IdentityErrorDescriber.DuplicateUserName)
        ];

        return result.Errors.Any(e => emailCodes.Contains(e.Code))
            ? new BadRequestException(
                new Dictionary<string, string[]> { ["email"] = ["Geçerli bir e-posta girin."] })
            : new BadRequestException("Kullanıcı güncellenemedi.");
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
