// Canonical URL query string for pages that keep list state in the URL (single source of
// truth). Writes only the given keys, in a fixed order, and drops any whose value equals its
// default — so the address bar stays stable instead of churning param order or leaving
// redundant defaults (e.g. `page=1`). Shared by Products, TedarikciDetay, KategoriDetay.
export function canonicalParams(
  source: URLSearchParams,
  order: readonly string[],
  defaults: Record<string, string> = {},
): URLSearchParams {
  const out = new URLSearchParams()
  for (const key of order) {
    const value = source.get(key)
    if (!value) continue
    if (defaults[key] === value) continue
    out.set(key, value)
  }
  return out
}
