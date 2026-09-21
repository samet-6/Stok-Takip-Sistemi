namespace StokTakip.Domain.Common;

/// <summary>
/// A row that can be taken out of service without leaving the database. <see cref="IsActive"/>
/// answers "can this still be chosen", <see cref="DeactivatedAt"/> answers "since when" — and
/// the second is written centrally when the first flips, never by the service doing the flip.
/// Null while active; null is also what an already-passive row carries if it was deactivated
/// before the stamp existed, so read it as "unknown", not as "just now".
/// </summary>
public interface IDeactivatable
{
    bool IsActive { get; set; }
    DateTime? DeactivatedAt { get; set; }
}
