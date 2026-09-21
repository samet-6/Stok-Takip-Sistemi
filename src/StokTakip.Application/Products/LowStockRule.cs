using System.Linq.Expressions;
using StokTakip.Domain.Entities;

namespace StokTakip.Application.Products;

/// <summary>
/// The single definition of "low stock". The list filter, the summary tile and the threshold
/// notification all read it from here — before this they each carried their own copy, and the
/// bell's copy said "&lt;" while the screen's said "&lt;=", so a product landing exactly on its
/// minimum turned red without anyone being told.
/// <para>
/// A passive product is not low, it is closed: it cannot take movements at all, so running out
/// is not something that can happen to it, and counting it would keep the warning tile lit.
/// </para>
/// </summary>
public static class LowStockRule
{
    /// <summary>Query form — what EF translates.</summary>
    public static readonly Expression<Func<Product, bool>> Predicate =
        p => p.IsActive && p.StockQuantity <= p.MinStockLevel;

    /// <summary>
    /// In-memory form, for the previous state the edge detector judges — there is no entity for
    /// "the product as it was before this movement". LowStockRuleTests pins both forms to the
    /// same truth table, boundary included.
    /// </summary>
    public static bool IsLow(bool isActive, int quantity, int minStockLevel) =>
        isActive && quantity <= minStockLevel;
}
