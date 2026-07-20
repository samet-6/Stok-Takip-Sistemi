using Microsoft.AspNetCore.Identity;

namespace StokTakip.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = null!;
}
