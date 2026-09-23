namespace StokTakip.Application.Common.Exceptions;

/// <summary>
/// A write that must be recognisable when it is sent again arrived without a usable key (D27).
/// Its own type rather than a BadRequestException so the response carries its own code: the
/// client's fix is "send the header", not "correct a field".
/// </summary>
public sealed class IdempotencyKeyRequiredException : Exception
{
    public IdempotencyKeyRequiredException()
        : base("Idempotency-Key başlığı gerekli (geçerli bir UUID).")
    {
    }
}
