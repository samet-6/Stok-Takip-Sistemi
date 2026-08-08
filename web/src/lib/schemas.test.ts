import { makeUserSchema, productSchema } from './schemas'

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
