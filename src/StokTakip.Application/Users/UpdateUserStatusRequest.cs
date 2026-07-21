namespace StokTakip.Application.Users;

// PATCH body: deactivate (İşten Çıkar) / reactivate (İşe Geri Al) a Çalışan.
public sealed class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}
