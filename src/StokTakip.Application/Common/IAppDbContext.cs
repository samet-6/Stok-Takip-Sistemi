using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using StokTakip.Domain.Entities;

namespace StokTakip.Application.Common;

public interface IAppDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Product> Products { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Notification> Notifications { get; }

    EntityEntry<T> Entry<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
