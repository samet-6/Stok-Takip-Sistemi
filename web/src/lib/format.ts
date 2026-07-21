const tryFormatter = new Intl.NumberFormat('tr-TR', {
  style: 'currency',
  currency: 'TRY',
})

const dateTimeFormatter = new Intl.DateTimeFormat('tr-TR', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

/** Formats a number as Turkish Lira, e.g. 1234.5 → "₺1.234,50". */
export function formatCurrency(value: number): string {
  return tryFormatter.format(value)
}

const dateFormatter = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
})

/** Formats a UTC ISO timestamp in the viewer's local time (tr-TR). */
export function formatDateTime(iso: string): string {
  return dateTimeFormatter.format(new Date(iso))
}

/** Formats a UTC ISO timestamp as gg.aa.yyyy (day precision, tr-TR). */
export function formatDate(iso: string): string {
  return dateFormatter.format(new Date(iso))
}
