import { newIdempotencyKey } from '../lib/idempotencyKey'

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

describe('newIdempotencyKey', () => {
  it('RFC 4122 v4 biçiminde bir UUID üretiyor', () => {
    expect(newIdempotencyKey()).toMatch(UUID_V4)
  })

  it('her çağrıda farklı anahtar üretiyor', () => {
    const keys = new Set(Array.from({ length: 200 }, () => newIdempotencyKey()))
    expect(keys.size).toBe(200)
  })

  // crypto.randomUUID exists only in secure contexts (https, localhost); the app opened over
  // plain http on a LAN address has none. The key must not depend on it.
  it('crypto.randomUUID olmadan da çalışıyor', () => {
    const original = crypto.randomUUID
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
    try {
      expect(newIdempotencyKey()).toMatch(UUID_V4)
    } finally {
      Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true })
    }
  })
})
