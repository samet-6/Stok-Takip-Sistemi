namespace StokTakip.Application.Common.Exceptions;

// Named BadRequestException (not ValidationException) to avoid clashing with
// System.ComponentModel.DataAnnotations.ValidationException.
public sealed class BadRequestException : Exception
{
    // Field-keyed validation errors surfaced as RFC 7807 `errors`
    // (e.g. { "password": [...] }); null for a plain single-message 400.
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(IReadOnlyDictionary<string, string[]> fieldErrors)
        : base("Doğrulama hatası.")
    {
        FieldErrors = fieldErrors;
    }
}
