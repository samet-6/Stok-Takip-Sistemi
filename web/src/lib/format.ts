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

// Date-range filter boundaries. A <input type="date"> yields a bare local calendar
// day (yyyy-mm-dd); the backend compares against a timestamptz (UTC) column and stays
// timezone-agnostic. So we convert the picked *local* day to an offset-aware UTC instant
// here (the only side that knows the viewer's timezone).

/** Local calendar day (yyyy-mm-dd) → ISO instant at the START of that day (UTC Z). */
export function dayStartIso(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString()
}

/** Local calendar day (yyyy-mm-dd) → ISO instant at the END of that day, inclusive (UTC Z). */
export function dayEndIso(date: string): string {
  return new Date(`${date}T23:59:59.999`).toISOString()
}
