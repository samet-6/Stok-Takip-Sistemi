namespace StokTakip.Application.Common.Exceptions;

// Named BadRequestException (not ValidationException) to avoid clashing with
// System.ComponentModel.DataAnnotations.ValidationException — intentional.
public sealed class BadRequestException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public BadRequestException(string message) : base(message)
    {
        Errors = [message];
    }

    public BadRequestException(IEnumerable<string> errors) : base("Bad request.")
    {
        Errors = errors.ToList();
    }
}
