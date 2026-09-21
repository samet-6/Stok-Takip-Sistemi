namespace StokTakip.Application.Common.Exceptions;

/// <summary>
/// Too many failed attempts. Kept apart from UnauthorizedException so the response can carry its
/// own code: the client has to tell "wrong password, try again" from "waiting is the only thing
/// that helps now", and those are different instructions for the user.
/// </summary>
public sealed class LockedOutException : Exception
{
    public LockedOutException(string message) : base(message)
    {
    }
}
