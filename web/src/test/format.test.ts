import { dayEndIso, dayStartIso, formatCurrency, formatDate, formatDateTime } from '../lib/format'

// Every assertion below is built from a LOCAL date, never a hardcoded UTC string: the day
// boundaries convert the viewer's calendar day to an instant, so a literal like
// '2026-03-15T00:00:00.000Z' would only hold in UTC+0 and fail on this machine.
const LOCAL_DAY = '2026-03-15'
const localMidnight = new Date(2026, 2, 15, 0, 0, 0, 0)
const localEndOfDay = new Date(2026, 2, 15, 23, 59, 59, 999)

describe('formatCurrency', () => {
  it('Türk Lirası biçimiyle yazıyor', () => {
    const formatted = formatCurrency(1234.5)

    expect(formatted).toContain('1.234,50')
    expect(formatted).toContain('₺')
  })

  it('kuruşu her zaman iki hane gösteriyor', () => {
    expect(formatCurrency(10)).toContain('10,00')
  })
})

describe('formatDate', () => {
  it('gg.aa.yyyy yazıyor', () => {
    expect(formatDate(localMidnight.toISOString())).toBe('15.03.2026')
  })
})

describe('formatDateTime', () => {
  // The contract is "UTC instant rendered in the viewer's local time" — asserting the wall
  // clock the viewer set is what proves the conversion happened, in any timezone.
  it('saati izleyicinin yerel saatiyle gösteriyor', () => {
    const afternoon = new Date(2026, 2, 15, 14, 30)

    expect(formatDateTime(afternoon.toISOString())).toContain('14:30')
  })
})

describe('gün sınırları', () => {
  it('dayStartIso yerel günün başlangıcını veriyor', () => {
    expect(dayStartIso(LOCAL_DAY)).toBe(localMidnight.toISOString())
  })

  it('dayEndIso yerel günün sonunu veriyor — son milisaniye dahil', () => {
    expect(dayEndIso(LOCAL_DAY)).toBe(localEndOfDay.toISOString())
  })

  it('ikisi de UTC (Z) damgası taşıyor', () => {
    expect(dayStartIso(LOCAL_DAY)).toMatch(/Z$/)
    expect(dayEndIso(LOCAL_DAY)).toMatch(/Z$/)
  })

  // A movement created at any moment of the picked day must fall inside the filter range;
  // this is the same inclusive-end contract the backend's date filter tests assert.
  it('aralık günün tamamını kapsıyor', () => {
    const start = new Date(dayStartIso(LOCAL_DAY)).getTime()
    const end = new Date(dayEndIso(LOCAL_DAY)).getTime()
    const noon = new Date(2026, 2, 15, 12, 0).getTime()

    expect(start).toBeLessThan(end)
    expect(noon).toBeGreaterThan(start)
    expect(noon).toBeLessThan(end)
    expect(end - start).toBe(86_399_999)
  })
})
