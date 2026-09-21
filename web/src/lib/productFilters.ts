// The two list switches are mutually exclusive by definition, not by taste: the server's "low
// stock" already means active products only, so asking for passives at the same time is a
// contradiction. Turning one on clears the other instead of leaving a dead parameter behind —
// the URL is this page's single source of truth, and a parameter with no effect would come back
// the moment the user flips the switch off.
const EXCLUSIVE_PAIR = {
  lowStockOnly: 'includeInactive',
  includeInactive: 'lowStockOnly',
} as const

export type ExclusiveFilter = keyof typeof EXCLUSIVE_PAIR

export function toggleExclusiveFilter(
  params: URLSearchParams,
  key: ExclusiveFilter,
  checked: boolean,
): URLSearchParams {
  const next = new URLSearchParams(params)

  if (checked) {
    next.set(key, 'true')
    next.delete(EXCLUSIVE_PAIR[key])
  } else {
    next.delete(key)
  }

  return next
}
