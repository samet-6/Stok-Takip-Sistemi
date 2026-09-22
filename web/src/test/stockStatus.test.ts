import { stockStatus } from '../lib/stockStatus'

describe('stockStatus', () => {
  it('stok 0 ise Tükendi diyor, Düşük değil', () => {
    expect(stockStatus(0, 5)).toEqual({ variant: 'crit', label: 'Tükendi' })
  })

  it('eşik 0 olsa da stok 0 Tükendi', () => {
    expect(stockStatus(0, 0)).toEqual({ variant: 'crit', label: 'Tükendi' })
  })

  it('stok eşiğin altında ise Düşük', () => {
    expect(stockStatus(3, 5)).toEqual({ variant: 'warn', label: 'Düşük' })
  })

  it('stok tam eşikte ise Düşük — eşik dahil', () => {
    expect(stockStatus(5, 5)).toEqual({ variant: 'warn', label: 'Düşük' })
  })

  it('stok eşiğin üstünde ise etiket yok', () => {
    expect(stockStatus(6, 5)).toBeNull()
  })
})
