namespace StokTakip.Application.Common.Exceptions;

/// <summary>
/// The key is already bound to a different write — other content, or another user (D27). Not a
/// retry, so answering with the first write's result would tell the caller something false
/// (e.g. that a changed quantity was saved). 422, as the IETF Idempotency-Key draft suggests.
/// </summary>
public sealed class IdempotencyKeyReusedException : Exception
{
    public IdempotencyKeyReusedException()
        : base("Bu işlem anahtarı daha önce farklı bir kayıt için kullanılmış.")
    {
    }
}
