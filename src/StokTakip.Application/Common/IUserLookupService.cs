namespace StokTakip.Application.Common;

public interface IUserLookupService
{
    Task<IReadOnlyDictionary<string, string>> GetFullNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken ct);
}
