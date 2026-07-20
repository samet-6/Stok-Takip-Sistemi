using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Infrastructure.Data;

namespace StokTakip.Infrastructure.Services;

// Application cannot reach Identity; it resolves user ids → FullName through this abstraction.
public sealed class UserLookupService : IUserLookupService
{
    private readonly AppDbContext _db;

    public UserLookupService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, string>> GetFullNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<string, string>();

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}
