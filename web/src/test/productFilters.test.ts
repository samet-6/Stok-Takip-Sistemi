import { toggleExclusiveFilter } from '../lib/productFilters'

const params = (init: Record<string, string>) => new URLSearchParams(init)

describe('Ürün listesi filtre kilidi', () => {
  it('düşük stok açılınca pasif filtresini URL’den düşürüyor', () => {
    const next = toggleExclusiveFilter(params({ includeInactive: 'true' }), 'lowStockOnly', true)

    expect(next.get('lowStockOnly')).toBe('true')
    expect(next.has('includeInactive')).toBe(false)
  })

  it('pasif filtresi açılınca düşük stok filtresini URL’den düşürüyor', () => {
    const next = toggleExclusiveFilter(params({ lowStockOnly: 'true' }), 'includeInactive', true)

    expect(next.get('includeInactive')).toBe('true')
    expect(next.has('lowStockOnly')).toBe(false)
  })

  it('kapatma yalnız kendi parametresini siliyor', () => {
    const next = toggleExclusiveFilter(
      params({ lowStockOnly: 'true', search: 'kalem' }),
      'lowStockOnly',
      false,
    )

    expect(next.has('lowStockOnly')).toBe(false)
    expect(next.get('search')).toBe('kalem')
  })

  it('ilgisiz parametrelere dokunmuyor ve girdiyi değiştirmiyor', () => {
    const input = params({ categoryId: '3', page: '2' })
    const next = toggleExclusiveFilter(input, 'lowStockOnly', true)

    expect(next.get('categoryId')).toBe('3')
    // Girdi kopyalanır: çağıranın elindeki nesne mutasyona uğramaz.
    expect(input.has('lowStockOnly')).toBe(false)
  })
})
