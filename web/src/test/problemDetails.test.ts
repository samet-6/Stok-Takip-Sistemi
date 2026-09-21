import { AxiosError, type AxiosResponse } from 'axios'
import { hasFieldErrors, parseProblemDetails, problemMessage } from '../lib/problemDetails'
import type { ProblemDetails } from '../types/api'

/** An axios rejection carrying a response body, the way the interceptor sees it. */
function axiosFailure(status: number, data: unknown): AxiosError {
  const error = new AxiosError(`Request failed with status code ${status}`)
  error.response = { status, data } as AxiosResponse
  return error
}

describe('parseProblemDetails', () => {
  // The whole point of the `code` contract: the frontend branches on the machine-readable
  // discriminator, so the body must survive normalization untouched.
  it('backend gövdesini olduğu gibi geçiriyor', () => {
    const body: ProblemDetails = {
      title: 'Yetersiz stok',
      status: 400,
      code: 'insufficient_stock',
      errors: { Quantity: ['Stok yetersiz'] },
    }

    expect(parseProblemDetails(axiosFailure(400, body))).toBe(body)
  })

  it('ProblemDetails olmayan gövdede durumu koruyup genel başlığa düşüyor', () => {
    const problem = parseProblemDetails(axiosFailure(502, '<html>Bad Gateway</html>'))

    expect(problem.status).toBe(502)
    expect(problem.title).toBe('Bir hata oluştu')
  })

  // Network failure: axios rejects with no response at all.
  it('yanıtsız axios hatasında genel başlık veriyor', () => {
    const problem = parseProblemDetails(new AxiosError('Network Error'))

    expect(problem.status).toBeUndefined()
    expect(problem.title).toBe('Bir hata oluştu')
    expect(problem.detail).toBe('Network Error')
  })

  it('axios olmayan hatayı da ProblemDetails şekline çeviriyor', () => {
    expect(parseProblemDetails(new Error('boom'))).toEqual({
      title: 'Beklenmeyen bir hata oluştu',
    })
  })

  it('hata olmayan bir değeri de yutmadan çeviriyor', () => {
    expect(parseProblemDetails(undefined).title).toBe('Beklenmeyen bir hata oluştu')
  })
})

describe('hasFieldErrors', () => {
  it('alan hatası varsa true', () => {
    expect(hasFieldErrors({ errors: { Email: ['Geçersiz'] } })).toBe(true)
  })

  // An empty map is what a non-validation 400 leaves behind; treating it as field errors
  // would show an empty form summary instead of the actual message.
  it('boş errors haritasında false', () => {
    expect(hasFieldErrors({ errors: {} })).toBe(false)
  })

  it('errors yoksa false', () => {
    expect(hasFieldErrors({ title: 'Bir hata oluştu' })).toBe(false)
  })
})

describe('problemMessage', () => {
  it('detail varsa onu seçiyor', () => {
    expect(problemMessage({ title: 'Başlık', detail: 'Ayrıntı' })).toBe('Ayrıntı')
  })

  it('detail yoksa title', () => {
    expect(problemMessage({ title: 'Başlık' })).toBe('Başlık')
  })

  it('ikisi de yoksa Türkçe genel mesaj', () => {
    expect(problemMessage({})).toBe('Bir hata oluştu')
  })
})
