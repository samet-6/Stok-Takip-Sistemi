import { catalogOptions } from '../lib/catalogOptions'

const ITEMS = [
  { id: 1, name: 'Gıda', isActive: true, contactEmail: 'gida@firma.com' },
  { id: 2, name: 'Eski', isActive: false, contactEmail: 'eski@firma.com' },
]

describe('catalogOptions', () => {
  it('aktif kaydı seçilebilir, adıyla veriyor', () => {
    expect(catalogOptions(ITEMS, undefined)[0]).toEqual({
      value: '1',
      label: 'Gıda',
      description: undefined,
      disabled: false,
    })
  })

  it('pasif kaydı işaretliyor ve seçilemez yapıyor', () => {
    const passive = catalogOptions(ITEMS, undefined)[1]

    expect(passive.label).toBe('Eski (Pasif)')
    expect(passive.disabled).toBe(true)
  })

  // A product whose category or supplier went passive stays editable instead of losing it.
  it('ürünün mevcut kaydı pasif olsa da seçilebilir kalıyor', () => {
    expect(catalogOptions(ITEMS, 2)[1].disabled).toBe(false)
  })

  it('alt satırı verilen fonksiyondan alıyor', () => {
    expect(catalogOptions(ITEMS, undefined, (s) => s.contactEmail)[0].description).toBe(
      'gida@firma.com',
    )
  })
})
