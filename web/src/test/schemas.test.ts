import { makeUserSchema, productSchema } from '../lib/schemas'

// 257 characters — one past the AspNetUsers.Email column — but a well-formed address, so the
// format rule cannot reject it in place of the length rule under test.
const TOO_LONG_EMAIL = 'u'.repeat(246) + '@stok.local'
const AT_LIMIT_EMAIL = 'u'.repeat(245) + '@stok.local'

const VALID_USER = { fullName: 'Test Çalışan', password: 'T3Yeni!2026' }

const VALID_PRODUCT = {
  name: 'Test Ürün',
  sku: 'TEST-01',
  categoryId: '1',
  supplierId: '1',
  unitPrice: '10',
  minStockLevel: '5',
  initialStock: '',
  isActive: true,
}

function emailIssues(email: string) {
  const result = makeUserSchema(false).safeParse({ ...VALID_USER, email })

  return result.success ? [] : result.error.issues.filter((i) => i.path[0] === 'email')
}

function descriptionIssues(description: string) {
  const result = productSchema.safeParse({ ...VALID_PRODUCT, description })

  return result.success ? [] : result.error.issues.filter((i) => i.path[0] === 'description')
}

describe('Çalışan formu e-posta sınırı', () => {
  it('256 karakteri kabul ediyor — sınır dahil', () => {
    expect(AT_LIMIT_EMAIL).toHaveLength(256)
    expect(emailIssues(AT_LIMIT_EMAIL)).toEqual([])
  })

  it('257 karakteri reddediyor', () => {
    expect(TOO_LONG_EMAIL).toHaveLength(257)
    expect(emailIssues(TOO_LONG_EMAIL).map((i) => i.message)).toContain(
      'En fazla 256 karakter olabilir',
    )
  })

  // react-hook-form shows the first issue for a field, so the order the rules run in is what
  // the user reads. An untouched field is empty, not malformed: telling them the address is
  // invalid when they have not typed one is the wrong sentence.
  it('boş alanda format değil zorunluluk mesajı veriyor', () => {
    expect(emailIssues('')[0]?.message).toBe('Bu alan zorunludur')
  })

  // The other half of the same rule: without this, an error callback that answered
  // "Bu alan zorunludur" to everything would satisfy the test above.
  it('bozuk adreste format mesajı veriyor', () => {
    expect(emailIssues('stok.local')[0]?.message).toBe('Geçerli bir e-posta girin')
  })

  // The limit is not a property of the create form: an edit that moves an account to an
  // oversized address reaches the same column.
  it('düzenleme modunda da geçerli', () => {
    const result = makeUserSchema(true).safeParse({
      ...VALID_USER,
      password: '',
      email: TOO_LONG_EMAIL,
    })

    expect(result.success).toBe(false)
  })
})

function skuIssues(sku: string) {
  const result = productSchema.safeParse({ ...VALID_PRODUCT, description: '', sku })

  return result.success ? [] : result.error.issues.filter((i) => i.path[0] === 'sku')
}

// D8 — mirrors the backend rule (the source of truth): an SKU is a code, not a word.
describe('Ürün formu SKU kuralı', () => {
  it.each(['TEST-01', 'ab-1.x/2_y', '  test-01  '])('%s kabul ediliyor', (sku) => {
    expect(skuIssues(sku)).toEqual([])
  })

  it.each(['ABı-1', 'ABÇ-1', 'AB 1', 'ABß', 'AB#1'])('%s reddediliyor', (sku) => {
    expect(skuIssues(sku).map((i) => i.message)).toContain(
      'Yalnız harf (A–Z), rakam ve . _ / - kullanılabilir',
    )
  })
})

describe('Ürün formu açıklama sınırı', () => {
  it('500 karakteri kabul ediyor — sınır dahil', () => {
    expect(descriptionIssues('a'.repeat(500))).toEqual([])
  })

  // 501 is where the varchar(500) column would have thrown; before the limits were derived from
  // the schema this form let 1000 characters through.
  it('501 karakteri reddediyor', () => {
    expect(descriptionIssues('a'.repeat(501)).map((i) => i.message)).toContain(
      'En fazla 500 karakter olabilir',
    )
  })
})
