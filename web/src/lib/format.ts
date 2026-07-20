const tryFormatter = new Intl.NumberFormat('tr-TR', {
  style: 'currency',
  currency: 'TRY',
})

/** Formats a number as Turkish Lira, e.g. 1234.5 → "₺1.234,50". */
export function formatCurrency(value: number): string {
  return tryFormatter.format(value)
}
