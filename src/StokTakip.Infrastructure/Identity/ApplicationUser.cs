using Microsoft.AspNetCore.Identity;

namespace StokTakip.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = null!;

    /// <summary>Soft-delete flag. Inactive users cannot log in; audit trail is preserved.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Account creation timestamp (UTC). Shown in the UI as "İşe Giriş".</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last deactivation timestamp (UTC); null while active. Shown as "İşten Çıkış". Cleared on reactivation.</summary>
    public DateTime? DeactivatedAt { get; set; }
}
