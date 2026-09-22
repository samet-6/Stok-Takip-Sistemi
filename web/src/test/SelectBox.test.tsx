import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SelectBox, type SelectOption } from '../components/SelectBox'

const OPTIONS: SelectOption[] = [
  { value: '1', label: 'Ege Kırtasiye', description: 'ege@firma-a.com' },
  { value: '2', label: 'Ege Kırtasiye', description: 'iletisim@firma-b.com' },
  { value: '3', label: 'Eski Tedarikçi (Pasif)', description: 'eski@firma.com', disabled: true },
]

function renderBox(value = '', options = OPTIONS, onChange = vi.fn()) {
  render(
    <SelectBox
      id="box"
      options={options}
      value={value}
      onChange={onChange}
      placeholder="Seçiniz…"
    />,
  )
  return onChange
}

describe('SelectBox', () => {
  it('boşken yer tutucuyu gösteriyor', () => {
    renderBox()

    expect(screen.getByRole('button', { name: 'Seçiniz…' })).toBeInTheDocument()
  })

  // Filters list "all" as a real, choosable option with an empty value.
  it('değeri boş olan bir seçenek varsa kapalıyken onun etiketini gösteriyor', () => {
    renderBox('', [{ value: '', label: 'Tüm kategoriler' }, { value: '1', label: 'Gıda' }])

    expect(screen.getByRole('button', { name: 'Tüm kategoriler' })).toBeInTheDocument()
  })

  it('açılınca aynı etiketli seçenekleri alt satırlarıyla ayırıyor', async () => {
    renderBox()

    await userEvent.click(screen.getByRole('button', { name: 'Seçiniz…' }))

    expect(screen.getByText('ege@firma-a.com')).toBeInTheDocument()
    expect(screen.getByText('iletisim@firma-b.com')).toBeInTheDocument()
  })

  // The description belongs to the list only: next to the label in the closed box it crowded
  // out the name and was cut off anyway.
  it('kapalı kutuda yalnız etiket var, alt satır yok', () => {
    renderBox('2')

    expect(screen.getByRole('button', { name: 'Ege Kırtasiye' })).toBeInTheDocument()
    expect(screen.queryByText('iletisim@firma-b.com')).not.toBeInTheDocument()
  })

  it('seçenek seçilince değerini veriyor', async () => {
    const onChange = renderBox()

    await userEvent.click(screen.getByRole('button', { name: 'Seçiniz…' }))
    await userEvent.click(screen.getByText('iletisim@firma-b.com'))

    expect(onChange).toHaveBeenCalledWith('2')
  })

  // Through the click, not an attribute: react-bootstrap disables an item with aria-disabled
  // and by ignoring the click, never with the native disabled attribute.
  it('devre dışı seçenek seçilemiyor', async () => {
    const onChange = renderBox()

    await userEvent.click(screen.getByRole('button', { name: 'Seçiniz…' }))
    await userEvent.click(screen.getByText('eski@firma.com'))

    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByText('eski@firma.com').closest('button')).toHaveAttribute('aria-disabled', 'true')
  })

  // The menu always opens downwards and must never make the page taller: whatever room is left
  // under the box is all it gets, and it scrolls inside that.
  it('açılan liste kutunun altındaki boşluğa sığacak kadar kısalıyor', async () => {
    renderBox()
    const toggle = screen.getByRole('button', { name: 'Seçiniz…' })
    vi.spyOn(toggle, 'getBoundingClientRect').mockReturnValue({ bottom: 600 } as DOMRect)
    vi.stubGlobal('innerHeight', 760)

    await userEvent.click(toggle)

    const menu = screen.getByText('ege@firma-a.com').closest('.dropdown-menu') as HTMLElement
    expect(menu.style.maxHeight).toBe('152px') // 760 - 600 - 8 px breathing room
    vi.unstubAllGlobals()
  })

  // Underneath it is still a .btn, whose padding would otherwise let a long label run under the
  // arrow — the right padding has to be the native select's.
  it('sağda ok için yerel seçim kutusu kadar boşluk bırakıyor', () => {
    renderBox()

    expect(screen.getByRole('button', { name: 'Seçiniz…' }).style.paddingRight).toBe('2.25rem')
  })
})
