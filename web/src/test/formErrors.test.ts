import { applyServerFieldErrors } from '../lib/formErrors'
import type { ProblemDetails } from '../types/api'

type Fields = { email: string; fullName: string }

const FIELDS = ['email', 'fullName'] as const

function apply(errors: ProblemDetails['errors']) {
  const setError = vi.fn()
  const matched = applyServerFieldErrors<Fields>({ errors } as ProblemDetails, setError, FIELDS)

  return { setError, matched }
}

describe('applyServerFieldErrors', () => {
  // The backend sends PascalCase property names ("Email"); the form fields are camelCase.
  // Without the case-insensitive match nothing lines up and the message is dropped silently.
  it('PascalCase sunucu anahtarını camelCase alana bağlıyor', () => {
    const { setError, matched } = apply({ Email: ['Bu e-posta zaten kayıtlı'] })

    expect(matched).toBe(true)
    expect(setError).toHaveBeenCalledWith('email', {
      type: 'server',
      message: 'Bu e-posta zaten kayıtlı',
    })
  })

  it('aynı alanın birden fazla mesajını tek metinde birleştiriyor', () => {
    const { setError } = apply({ fullName: ['Zorunludur', 'En fazla 100 karakter'] })

    expect(setError).toHaveBeenCalledWith('fullName', {
      type: 'server',
      message: 'Zorunludur En fazla 100 karakter',
    })
  })

  it('birden fazla alanı aynı anda işaretliyor', () => {
    const { setError, matched } = apply({ Email: ['Geçersiz'], FullName: ['Zorunludur'] })

    expect(matched).toBe(true)
    expect(setError).toHaveBeenCalledTimes(2)
  })

  // These three are the "silently swallowed" paths: the caller uses the false return to fall
  // back to a toast, so a wrong `true` here means the user sees nothing at all.
  it('errors alanı yoksa false dönüyor', () => {
    const { setError, matched } = apply(undefined)

    expect(matched).toBe(false)
    expect(setError).not.toHaveBeenCalled()
  })

  it('forma ait olmayan alan adı için false dönüyor', () => {
    const { setError, matched } = apply({ StockQuantity: ['Yetersiz stok'] })

    expect(matched).toBe(false)
    expect(setError).not.toHaveBeenCalled()
  })

  it('mesaj listesi boşsa alanı işaretlemiyor', () => {
    const { setError, matched } = apply({ Email: [] })

    expect(matched).toBe(false)
    expect(setError).not.toHaveBeenCalled()
  })
})
