import { canonicalParams } from '../lib/urlParams'

const ORDER = ['search', 'page', 'sort'] as const

function canonical(query: string, defaults?: Record<string, string>) {
  return canonicalParams(new URLSearchParams(query), ORDER, defaults).toString()
}

describe('canonicalParams', () => {
  it('anahtarları kaynaktaki değil verilen sırayla yazıyor', () => {
    expect(canonical('sort=name&page=2&search=kalem')).toBe('search=kalem&page=2&sort=name')
  })

  // The churn this prevents: typing in the search box rewrote the URL with `page=1` still
  // attached, so every keystroke pushed a history entry that differed only in param order.
  it('varsayılana eşit değeri düşürüyor', () => {
    expect(canonical('search=kalem&page=1', { page: '1' })).toBe('search=kalem')
  })

  it('varsayılandan farklı değeri koruyor', () => {
    expect(canonical('search=kalem&page=2', { page: '1' })).toBe('search=kalem&page=2')
  })

  it('boş değeri düşürüyor', () => {
    expect(canonical('search=&page=2')).toBe('page=2')
  })

  it('sırada olmayan anahtarı taşımıyor', () => {
    expect(canonical('search=kalem&tracking=abc')).toBe('search=kalem')
  })

  it('aynı girdi için aynı çıktıyı veriyor — sıra kaynaktan bağımsız', () => {
    expect(canonical('page=2&search=kalem')).toBe(canonical('search=kalem&page=2'))
  })
})
